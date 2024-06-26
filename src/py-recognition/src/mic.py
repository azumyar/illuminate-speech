import queue
import time
import multiprocessing
import speech_recognition as sr
import speech_recognition.exceptions
from concurrent.futures import ThreadPoolExecutor
from typing import Any, Callable, Deque, NamedTuple

import src.recognition as recognition
import src.exception as ex
import src.filter as filter
import src.val as val
from src.cancellation import CancellationObject

class ListenEnergy(NamedTuple):
    value:float
    max:float
    min:float


class ListenResultParam(NamedTuple):
    '''listenのコールバックパラメータ'''
    pcm:bytes
    '''PCMバイナリデータ'''
    energy:ListenEnergy | None
    '''pcmのRMS値(src.mic.AudioDataのみに格納)'''


class AudioData(sr.AudioData):
    def __init__(self, frame_data:bytes, sample_rate:int, sample_width:int, energy:ListenEnergy):
        super().__init__(frame_data, sample_rate, sample_width)
        self.__energy = energy

    @property
    def energy(self) -> ListenEnergy: return self.__energy

class Recognizer(sr.Recognizer):
    __PAPUSE_MIN_THREHOLD = 0.4
    '''Recognizer#pause_thresholdがこの値より未満の場合特殊処理を行います'''

    def __init__(self):
        super().__init__()
        self.dynamic_energy_min:float = 100.0
        self.is_tail_cut = False
        self.delay_duration = 0.
        self.filter_highPass:filter.NoiseFilter|None = None
        '''adjust_for_ambient_noise()およびdynamic_energy_thresholdがTrueの場合energy_thresholdがとる最低の値'''

    def adjust_for_ambient_noise(self, source:sr.AudioSource, duration:float=1.0) -> None:
        super().adjust_for_ambient_noise(source, duration) # type: ignore
        self.energy_threshold = max(self.dynamic_energy_min, self.energy_threshold)

    def listen(self, source, timeout=None, phrase_time_limit=None, snowboy_configuration=None) -> sr.AudioData:
        import collections
        import audioop
        import math
        import os
        import numpy

        assert isinstance(source, sr.Microphone), "Source must be an audio source"
        assert source.stream is not None, "Audio source must be entered before listening, see documentation for ``AudioSource``; are you using ``source`` outside of a ``with`` statement?"
        assert self.pause_threshold >= self.non_speaking_duration >= 0
        if snowboy_configuration is not None:
            assert os.path.isfile(os.path.join(snowboy_configuration[0], "snowboydetect.py")), "``snowboy_configuration[0]`` must be a Snowboy root directory containing ``snowboydetect.py``"
            for hot_word_file in snowboy_configuration[1]:
                assert os.path.isfile(hot_word_file), "``snowboy_configuration[1]`` must be a list of Snowboy hot word configuration files"

        seconds_per_buffer = float(source.CHUNK) / source.SAMPLE_RATE
        pause_buffer_count = int(math.ceil(self.pause_threshold / seconds_per_buffer))  # number of buffers of non-speaking audio during a phrase, before the phrase should be considered complete
        phrase_buffer_count = int(math.ceil(self.phrase_threshold / seconds_per_buffer))  # minimum number of buffers of speaking audio before we consider the speaking audio a phrase
        non_speaking_buffer_count = int(math.ceil(self.non_speaking_duration / seconds_per_buffer))  # maximum number of buffers of non-speaking audio to retain before and after a phrase

        # read audio input for phrases until there is a phrase that is long enough
        elapsed_time = 0  # number of seconds of audio read
        buffer = b""  # an empty buffer means that the stream has ended and there is no data left to read
        pause_count = 0
        while True:
            frames = collections.deque()

            if snowboy_configuration is None:
                # store audio input until the phrase starts
                while True:
                    time.sleep(0.01)

                    # handle waiting too long for phrase by raising an exception
                    elapsed_time += seconds_per_buffer
                    if timeout and elapsed_time > timeout:
                        raise sr.WaitTimeoutError("listening timed out while waiting for phrase to start")

                    buffer = source.stream.read(source.CHUNK)
                    if len(buffer) == 0: break  # reached end of the stream
                    buffer = self.filter(buffer)
                    energy = audioop.rms(buffer, source.SAMPLE_WIDTH)  # energy of the audio signal
                    frames.append((buffer, energy))
                    if len(frames) > non_speaking_buffer_count:  # ensure we only keep the needed amount of non-speaking buffers
                        frames.popleft()

                    # detect whether speaking has started on audio input
                    if energy > self.energy_threshold: break

                    # dynamically adjust the energy threshold using asymmetric weighted average
                    if self.dynamic_energy_threshold:
                        damping = self.dynamic_energy_adjustment_damping ** seconds_per_buffer  # account for different chunk sizes and rates
                        target_energy = energy * self.dynamic_energy_ratio
                        self.energy_threshold = max(self.dynamic_energy_min, self.energy_threshold * damping + target_energy * (1 - damping))
            else:
                # read audio input until the hotword is said
                snowboy_location, snowboy_hot_word_files = snowboy_configuration
                buffer, delta_time = self.snowboy_wait_for_hot_word(snowboy_location, snowboy_hot_word_files, source, timeout)
                elapsed_time += delta_time
                if len(buffer) == 0: break  # reached end of the stream
                frames.append(buffer)

            # read audio input until the phrase ends
            pause_count, phrase_count = 0, 0
            phrase_start_time = elapsed_time
            while True:
                # handle phrase being too long by cutting off the audio
                elapsed_time += seconds_per_buffer
                if phrase_time_limit and elapsed_time - phrase_start_time > phrase_time_limit:
                    break

                buffer = source.stream.read(source.CHUNK)
                if len(buffer) == 0: break  # reached end of the stream
                buffer = self.filter(buffer)
                energy = audioop.rms(buffer, source.SAMPLE_WIDTH)  # unit energy of the audio signal within the buffer
                frames.append((buffer, energy))
                phrase_count += 1

                # check if speaking has stopped for longer than the pause threshold on the audio input
                if energy > self.energy_threshold:
                    pause_count = 0
                else:
                    pause_count += 1
                if pause_count > pause_buffer_count:  # end of the phrase
                    break
                time.sleep(0.01)

            # check how long the detected phrase is, and retry listening if the phrase is too short
            phrase_count -= pause_count  # exclude the buffers for the pause before the phrase
            if phrase_count >= phrase_buffer_count or len(buffer) == 0: break  # phrase is long enough or we've reached the end of the stream, so stop listening

        def eng(buf:Deque[tuple[bytes, float]], threshold:float) -> ListenEnergy:
            top = None
            last = None
            b = list(buf)
            for i in range(len(b)):
                if not top is None and not last is None:
                    break
                if top is None:
                    if threshold < (b[i])[1]:
                        top = i
                li = len(buf) - 1 - i
                if last is None:
                    if threshold < (b[li])[1]:
                        last = li
            assert not top is None
            assert not last is None
            assert top < last
            b = b[top:last]

            return ListenEnergy(
                sum(map(lambda x: x[1], b)) / len(b),
                max(map(lambda x: x[1], b)),
                min(map(lambda x: x[1], b)))

        def gen_fade(buffer:bytes, duration:float) -> bytes | bytearray:
            def fade(i): return 0 #int((mx - i) / mx * last)
            last:int
            mx = math.ceil(source.SAMPLE_RATE * duration)

            assert source.SAMPLE_WIDTH in [2, 3]
            if source.SAMPLE_WIDTH == 2:
                l, u = int(buffer[len(buffer) - 2]), int(buffer[len(buffer) - 1])
                last = l | (u << 8) & 0xffff # 大きな値になることがあるので一度保留
                return numpy.array(list(map(fade, range(mx))), numpy.uint16).tobytes("C")
            elif source.SAMPLE_WIDTH == 3:
                # 使わないけど一応作っておく
                l, m, u = buffer[len(buffer) - 3], buffer[len(buffer) - 2], buffer[len(buffer) - 1]
                last = l | (m << 8) | (u << 16)
                b = bytearray()
                for i in map(fade, range(mx)):
                    b.append(i & 0xff)
                    b.append(i >> 8 & 0xff)
                    b.append(i >> 16 & 0xff)
                return b
            raise ex.ProgramError()

        if not self.is_tail_cut:
            for _ in range(pause_count - non_speaking_buffer_count): frames.pop()  # remove extra non-speaking frames at the end
        engy_p = eng(frames, self.energy_threshold)
        if 0 < self.delay_duration:
            frames.append((gen_fade(buffer, self.delay_duration), 0))
        frame_data = b"".join(map(lambda x: x[0], frames))        
        return AudioData(frame_data, source.SAMPLE_RATE, source.SAMPLE_WIDTH, engy_p)
    
    @property
    def end_insert_sec(self) -> float:
        return self.delay_duration

    def filter(self, buffer:bytes) -> bytes:
        import numpy as np
        if self.filter_highPass is None:
            return buffer
        else:
            fft = np.fft.fft(np.frombuffer(buffer, np.int16).flatten())
            self.filter_highPass.filter(fft)
            return np.real(np.fft.ifft(fft)).astype(np.uint16, order="C").tobytes()

class MicParamObject(NamedTuple):
    device_index:int|None
    device_name:str
    sample_rate:int
    ambient_noise_to_energy:bool
    energy_threshold:float
    phrase_threshold:float|None
    pause_threshold:float
    non_speaking_duration:float|None
    dynamic_energy:bool
    dynamic_energy_ratio:float|None
    dynamic_energy_adjustment_damping:float|None
    dynamic_energy_min:float
    delay_duration:float


class Mic:
    """
    マイク操作クラス
    """

    __HOSTAPI_WASAPI = 2
    __HOSTAPI_KS = 3

    def __init__(
        self,
        sample_rate:int,
        ambient_noise_to_energy:bool,
        energy:float,
        pause:float,
        dynamic_energy:bool,
        dynamic_energy_ratio:float|None,
        dynamic_energy_adjustment_damping:float|None,
        dynamic_energy_min:float,
        phrase:float | None,
        non_speaking:float | None,
        listen_interval:float,
        recoginize_config:recognition.RecognizeMicrophoneConfig,
        filter_highPass:filter.NoiseFilter|None,
        mic_index:int | None) -> None:

        def check_mic(audio, mic_index:int, sample_rate:int):
            try:
                s = sr.Microphone.MicrophoneStream(
                    audio.open(
                        input = True,
                        input_device_index = mic_index,
                        channels = 1,
                        format = sr.Microphone.get_pyaudio().paInt16,
                        rate = sample_rate,
                        frames_per_buffer = 1024,
                    ))
                s.close()
            except Exception as e:
                raise MicInitializeExeception("マイクの初期化に失敗しました", e)
            finally:
                pass

        audio = sr.Microphone.get_pyaudio().PyAudio()
        try:
            if not mic_index is None:
                check_mic(audio, mic_index, sample_rate)
                self.__device_name = str(audio.get_device_info_by_index(mic_index).get("name"))
            else:
                self.__device_name = "デフォルトマイク"
        finally:
            audio.terminate()

        self.__initilaze_param = MicParamObject(
            device_index = mic_index,
            device_name = self.__device_name,
            sample_rate = sample_rate,
            ambient_noise_to_energy = ambient_noise_to_energy,
            energy_threshold = energy,
            phrase_threshold = phrase,
            pause_threshold = pause,
            non_speaking_duration = non_speaking,
            dynamic_energy = dynamic_energy,
            dynamic_energy_ratio = dynamic_energy_ratio,
            dynamic_energy_adjustment_damping = dynamic_energy_adjustment_damping,
            dynamic_energy_min = dynamic_energy_min,
            delay_duration = recoginize_config.delay_duration
        )

        self.__mic_index = mic_index
        self.__energy = energy
        self.__ambient_noise_to_energy = ambient_noise_to_energy
        self.__pause = pause
        self.__dynamic_energy = dynamic_energy
        self.__dynamic_energy_ratio = dynamic_energy_ratio
        self.__dynamic_energy_adjustment_damping = dynamic_energy_adjustment_damping
        self.__dynamic_energy_min = dynamic_energy_min
        self.__phrase = phrase
        self.__non_speaking = non_speaking
        self.__listen_timeout = listen_interval
        self.__recoginize_config = recoginize_config
        self.__filter_highPass = filter_highPass

        self.__sample_rate = sample_rate
        self.__audio_queue = queue.Queue()
        self.__source = Mic.__create_mic(sample_rate, mic_index)        
        self.__recorder = Mic.__create_recognizer(
            energy,
            pause,
            dynamic_energy,
            dynamic_energy_ratio,
            dynamic_energy_adjustment_damping,
            dynamic_energy_min,
            phrase,
            non_speaking,
            recoginize_config,
            filter_highPass)

        if ambient_noise_to_energy:
            with self.__source as mic:
                self.__recorder.adjust_for_ambient_noise(mic)

    def __enter__(self): return self.__source.__enter__()
    def __exit__(self, exc_type, exc_value, traceback): return self.__source.__exit__(exc_type, exc_value, traceback)

    @staticmethod
    def __create_mic(sample_rate:int, mic_index:int | None) -> sr.Microphone:
        return sr.Microphone(
            sample_rate = sample_rate,
            device_index = mic_index)

    @staticmethod
    def __create_recognizer(
        energy:float,
        pause:float,
        dynamic_energy:bool,
        dynamic_energy_ratio:float|None,
        dynamic_energy_adjustment_damping:float|None,
        dynamic_energy_min:float,
        phrase:float | None,
        non_speaking:float | None,
        recoginize_config:recognition.RecognizeMicrophoneConfig,
        filter_highPass:filter.NoiseFilter|None) -> Recognizer:

        r = Recognizer()
        r.energy_threshold = energy
        r.pause_threshold = pause
        if non_speaking is None:
            if r.pause_threshold < r.non_speaking_duration:
                r.non_speaking_duration = pause
        else:
            r.non_speaking_duration = non_speaking
        if not phrase is None:
            r.phrase_threshold = phrase
        r.dynamic_energy_threshold = dynamic_energy
        if not dynamic_energy_ratio is None:
            r.dynamic_energy_ratio = dynamic_energy_ratio
        if not dynamic_energy_adjustment_damping is None:
            r.dynamic_energy_adjustment_damping = dynamic_energy_adjustment_damping
        r.dynamic_energy_min = dynamic_energy_min
        r.delay_duration = recoginize_config.delay_duration
        r.filter_highPass = filter_highPass
        return r

    @staticmethod
    def update_sample_rate(mic_index:int | None, sample_rate:int) -> int:
        ret = sample_rate
        if not mic_index is None:
            audio = sr.Microphone.get_pyaudio().PyAudio()
            try:
                device_info = audio.get_device_info_by_index(mic_index)
                host = device_info.get("hostApi")
                # WASAPI共有モードはサンプリングレートをデバイスと一致させる必要がある
                if isinstance(host, int) and host == Mic.__HOSTAPI_WASAPI:
                    rate = device_info.get("defaultSampleRate")
                    assert isinstance(rate, float)
                    ret = int(rate)
            finally:
                audio.terminate()
        return ret
    
    @staticmethod
    def choice_mic(name:str, api:str) -> int | None:
        """
        名前とAPIからマイクを検索する
        """
        audio = speech_recognition.Microphone.get_pyaudio().PyAudio()
        try:
            host_api_index:int | None = None
            api_name = {
                val.MIC_API_VALUE_MME: lambda: "MME",
                val.MIC_API_VALUE_WASAPI: lambda: "Windows WASAPI",
            }[api]()
            for i in range(audio.get_host_api_count()):
                host_api = audio.get_host_api_info_by_index(i)
                
                if host_api.get("name") == api_name:
                    host_api_index = i
                    break
            if host_api_index is None:
                return None

            for i in range(audio.get_device_count()):
                device_info = audio.get_device_info_by_index(i)
                host_api = device_info.get("hostApi")
                device_name = device_info.get("name")
                if isinstance(host_api, int) and (host_api == host_api_index):
                    if isinstance(device_name, str) and (name.lower() in device_name.lower()):
                        return i
        finally:
            audio.terminate()

        return None
    
    @property
    def device_name(self):
        return self.__device_name
    
    @property
    def initilaze_param (self):
        return self.__initilaze_param

    @property
    def current_param (self):
        return MicParamObject(
            device_index = self.__initilaze_param.device_index,
            device_name =  self.__initilaze_param.device_name,
            sample_rate = self.__initilaze_param.sample_rate,
            ambient_noise_to_energy = self.__initilaze_param.ambient_noise_to_energy,
            energy_threshold = self.__recorder.energy_threshold,
            phrase_threshold = self.__recorder.phrase_threshold,
            pause_threshold = self.__recorder.pause_threshold,
            non_speaking_duration = self.__recorder.non_speaking_duration,
            dynamic_energy = self.__recorder.dynamic_energy_threshold,
            dynamic_energy_ratio = self.__recorder.dynamic_energy_ratio,
            dynamic_energy_adjustment_damping = self.__recorder.dynamic_energy_adjustment_damping,
            dynamic_energy_min = self.__recorder.dynamic_energy_min,
            delay_duration = self.__recoginize_config.delay_duration
        )


    @property
    def sample_rate(self):
        return self.__sample_rate

    @property
    def end_insert_sec(self) -> float:
        return self.__recorder.end_insert_sec

    def get_mic_info_(self) -> str:
        import os
        return os.linesep.join([
            f"initial-info",
            f"device : {self.__mic_index}",
            f"energy_threshold : {round(self.__energy, 2)}",
            f"ambient_noise_to_energy : {self.__ambient_noise_to_energy}",
            f"dynamic_energy : {self.__dynamic_energy}",
            f"dynamic_energy_ratio : {self.__dynamic_energy_ratio}",
            f"dynamic_energy_adjustment_damping : {self.__dynamic_energy_adjustment_damping}",
            f"dynamic_energy_min : {self.__dynamic_energy_min}",
            f"pause : {round(self.__pause, 2)}",
            f"phrase : {self.__phrase if self.__phrase is None else round(self.__phrase, 2)}",
            f"non_speaking : {self.__non_speaking if self.__non_speaking is None else round(self.__non_speaking, 2)}",
            "",
            "current-info",
            f"device : {self.__device_name}",
            f"energy_threshold : {round(self.__recorder.energy_threshold,2)}",
            f"dynamic_energy : {self.__recorder.dynamic_energy_threshold}",
            f"dynamic_energy_ratio : {self.__recorder.dynamic_energy_ratio}",
            f"dynamic_energy_adjustment_damping : {self.__recorder.dynamic_energy_adjustment_damping}",
            f"pause : {round(self.__recorder.pause_threshold, 2)}",
            f"phrase : {round(self.__recorder.phrase_threshold, 2)}",
            f"non_speaking : {round(self.__recorder.non_speaking_duration, 2)}",
        ])

    def get_verbose(self, verbose:int) -> str | None:
        if verbose < 2:
            return None
        if not self.__dynamic_energy:
            return None
        return f"current energy_threshold = {self.__recorder.energy_threshold}"


    def __get_audio_data(self, min_time:float=-1.) -> bytes:
        audio = bytes()
        is_goted = False
        start_time = time.time()
        while not is_goted or time.time() - start_time < min_time:
            time.sleep(0.01)
            while not self.__audio_queue.empty():
                audio += self.__audio_queue.get()
                is_goted = True
        return sr.AudioData(audio, self.__sample_rate, 2).get_raw_data()

    def listen(self, onrecord:Callable[[int, ListenResultParam], None], timeout=None, phrase_time_limit=None) -> None:
        """
        一度だけマイクを拾う
        """
        try:
            with self.__source as microphone:
                audio = self.__recorder.listen(
                    source = microphone,
                    timeout = timeout,
                    phrase_time_limit = phrase_time_limit)
            energy:ListenEnergy|None = None
            if isinstance(audio, AudioData):
                energy = audio.energy 
            onrecord(1, ListenResultParam(audio.get_raw_data(), energy))
        except sr.WaitTimeoutError:
            pass
        except sr.UnknownValueError:
            pass

    def listen_loop(self, onrecord:Callable[[int, ListenResultParam], None], cancel:CancellationObject, phrase_time_limit=None) -> None:
        """
        マイクループ。キャンセルするまで処理を返しません。
        """
        def listen_mic():
            with self.__source as s:
                while cancel.alive:
                    try:
                        audio = self.__recorder.listen(s, self.__listen_timeout, phrase_time_limit)
                    except speech_recognition.exceptions.WaitTimeoutError:
                        pass
                    else:
                        if cancel.alive:
                            q.put_nowait(audio)

        q = queue.Queue()
        thread_pool = ThreadPoolExecutor(max_workers=2)
        thread_pool.submit(listen_mic)
        try:
            index = 1
            while cancel.alive:
                if not q.empty():
                    audio = q.get()
                    energy:ListenEnergy|None = None
                    if isinstance(audio, AudioData):
                        energy = audio.energy 
                    onrecord(index, ListenResultParam(audio.get_raw_data(), energy))
                    index += 1
                time.sleep(0.1)
        finally:
            thread_pool.shutdown(wait=False)


    def test_mic(self, timemax, onstart:Callable[[int], None], onend:Callable[[int, ListenResultParam|None], None], cancel:CancellationObject) -> None:
        """
        マイクテスト。キャンセルするまで処理を返しません。
        """
        try:
            index = 1
            while cancel.alive:
                onstart(index)
                audio:sr.AudioData | None = None
                for _ in range(int(timemax / self.__listen_timeout)):
                    try:
                        with self.__source as microphone:
                            audio = self.__recorder.listen(
                                source = microphone,
                                timeout = self.__listen_timeout,
                                phrase_time_limit = None)
                    except speech_recognition.exceptions.WaitTimeoutError:
                        pass
                    else:
                        break
                if audio is None:
                    onend(index, None)
                else:
                    b = audio.get_raw_data()
                    e = None
                    if isinstance(audio, AudioData):
                        e = audio.energy
                    onend(index, ListenResultParam(b, e))
                index += 1
                time.sleep(1)
        finally:
            pass

class MicInitializeExeception(ex.IlluminateException):
    pass