using Fleck;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Yarukizero.Net.IlluminateSpeech.Recognitions.ChromiumRecognition;
public class Plugin : IIlluminateRecognition {
	private string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
	private readonly string htmlTemplate = @"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <title>るみすぴ</title>
  <style>
:root {
  font-family:""Yu Gothic UI"", sans-serif;
}
body {
  margin: 0;
  padding: 0;
}
#c1 {
  display: grid;
  width: 100vw;
  height: 100vh;
  grid-template-rows: 20px 1fr;
  place-items: center;
}
#state { font-size: 16px; }
#recognition { font-size: 20px; }
  </style>
</head>
<body>
  <div id='c1'>
    <div id='state'></div>
    <div id='recognition'></div>
  </div>
<script>
function ws() {
  const elState = document.querySelector('#state');
  let pingPong = null;
  let con = new WebSocket('ws://localhost:$$ws-port$$');
  con.onopen = (event) => {
    elState.innerHTML = '認識待機中';
  };
  con.onmessage = (event) => {
    if(event.data == 'exit') {
      window.close();
    } else if(event.data == 'pong') {
      clearTimeout(pingPong);
    }
  };
  setTimeout(() => {
    con.send('ping');
    pingPongTimer = setTimeout(() => {
      pingPong = null
      con = ws();
    }, 1000);
  }, 30000);
  return con;
}
  const elState = document.querySelector('#state');
  const el = document.querySelector('#recognition');
  let con = ws();

  elState.innerHTML = '準備中…';

  SpeechRecognition = webkitSpeechRecognition || SpeechRecognition;
  const recognition = new SpeechRecognition();
  recognition.lang = 'ja-JP';
  recognition.interimResults = true;
  recognition.onresult = (event) => {
    let transcript = '';
    let finish = false;
    for(let i = event.resultIndex; i<event.results.length; i++) {
      transcript += event.results[i][0].transcript;
      finish |= event.results[i].isFinal;
    }
    elState.innerHTML = finish ? '認識待機中' : '認識中…';
    el.innerHTML = finish ? transcript : `<font color='#999'>${transcript}</font>`;
    con.send(JSON.stringify({ transcript, finish }));
  }
  recognition.onend = (event) => {
    console.log('onend')
    recognition.start(); // continuousではなんか止まるので明示的に止まったらスタートする
  }
  recognition.onsoundend = (event) => {
    console.log('onsoundend')
  }
  recognition. onerror  = (event) => {
    console.log('onerror')
  }
  recognition.start();
  window.resizeTo(240, 240);
</script>
</body>
</html>
";
	public int HttpPort { get; private set; } = 49513;
	public int WebSocketPort { get; private set; } = 49514;

	public void InitPlugin() { }
	public void DeinitPlugin() { }

	public IObservable<RecognitionObject> Run(CancellationToken cancellationToken) {
		return Observable.Create<RecognitionObject>(o => {
			IDisposable? httpSubscriber = null;
			IDisposable? wsSubscriber = null;
			var cancellationSource = new CancellationTokenSource();
			try {
				httpSubscriber = Observable.Create<int>(async o => {
					try {
						using var listener = new HttpListener();
						listener.Prefixes.Clear();
						listener.Prefixes.Add($"http://localhost:{this.HttpPort}/");
						listener.Start();

						while(!cancellationSource.Token.IsCancellationRequested) {
							var context = await listener.GetContextAsync();
							var request = context.Request;
							var response = context.Response;
							try {
								// リクエストの詳細は無視する
								if(request != null) {
									var text = Encoding.UTF8.GetBytes(htmlTemplate.Replace("$$ws-port$$", WebSocketPort.ToString()));
									response.OutputStream.Write(text, 0, text.Length);
								} else {
									response.StatusCode = 404;
								}
							}
							finally {
								response.Close();
							}
						}
					}
					finally {
						o.OnCompleted();
					}
					return System.Reactive.Disposables.Disposable.Empty;
				}).SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default)
					.Subscribe();
				wsSubscriber = Observable.Create<RecognitionObject>(oo => {
					try {
						using var server = new Fleck.WebSocketServer($"ws://127.0.0.1:{WebSocketPort}");

						IWebSocketConnection? soc = null;
						server.Start(socket => {
							soc = socket;
							socket.OnMessage = message => {
								try {
									if(message == "ping") {
										var _ = socket.Send("pong");
									} else {
										var json = Newtonsoft.Json.JsonConvert.DeserializeObject<RecognitionObject?>(message);
										if(json != null) {
											oo.OnNext(json);
										}
									}
								}
								catch(Exception e) {
									Console.Error.WriteLine(e);
								}
							};
						});
						cancellationSource.Token.WaitHandle.WaitOne();
						try {
							soc?.Send("exit").Wait();
						}
						catch(AggregateException) { }
					}
					finally { }
					return System.Reactive.Disposables.Disposable.Empty;
				}).SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default)
					.Subscribe(x => {
						o.OnNext(x);
					});

				using var process = Process.Start(new ProcessStartInfo() {
					FileName = chrome,
					Arguments = $"--disable-extensions --single-process -app=http://localhost:{this.HttpPort}/ --unsafely-treat-insecure-origin-as-secure=http://localhost:{this.HttpPort}/",
					CreateNoWindow = false,
					UseShellExecute = false,
				});
				cancellationToken.WaitHandle.WaitOne();
			}
			finally {
				httpSubscriber?.Dispose();
				wsSubscriber?.Dispose();
				cancellationSource.Cancel();
				o.OnCompleted();
			}
			return System.Reactive.Disposables.Disposable.Empty;
		}).SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default);
	}
}
