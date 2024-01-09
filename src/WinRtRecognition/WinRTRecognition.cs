using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media.SpeechRecognition;

namespace Yarukizero.Net.IlluminateSpeech.Recognitions.WinRTRecognition {
	public class Plugin : IIlluminateRecognition {
		private SpeechRecognizer? speechRecognizer = null;

		public void InitPlugin() {}

		public void DeinitPlugin() {}

		public IObservable<RecognitionObject> Run(CancellationToken cancellationToken) {
			return Observable.Create<RecognitionObject>(async o => {
				try {
					do {
						try {
							await this.Recognize(o);
							await Task.Delay(1);
						}
						catch(COMException e) {
							unchecked {
								if(e.HResult == (int)0x80045509) {
									await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-accounts"));
									break;
								}
							}
						}
					} while(!cancellationToken.IsCancellationRequested);
				}
				finally {
					this.speechRecognizer?.Dispose();
					this.speechRecognizer = null;
					o.OnCompleted();
				}
				return System.Reactive.Disposables.Disposable.Empty;
			});
		}

		private async Task Recognize(IObserver<RecognitionObject> observer) {
			void onHypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args) {
				observer.OnNext(new RecognitionObject() {
					Transcript = args.Hypothesis.Text,
					IsFinish = false,
				});
			}

			void onResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args) {
				observer.OnNext(new RecognitionObject() {
					Transcript = args.Result.Text,
					IsFinish = true,
				});
			}

			async void onCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args) {
				await Recognize(observer);
			}

			if(this.speechRecognizer != null) {
				this.speechRecognizer.HypothesisGenerated -= onHypothesisGenerated;
				this.speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= onResultGenerated;
				this.speechRecognizer.ContinuousRecognitionSession.Completed -= onCompleted;
				this.speechRecognizer.Dispose();
				this.speechRecognizer = null;
			}
			this.speechRecognizer = new SpeechRecognizer(new Language("ja"));
			this.speechRecognizer.HypothesisGenerated += onHypothesisGenerated;
			this.speechRecognizer.ContinuousRecognitionSession.ResultGenerated += onResultGenerated;
			this.speechRecognizer.ContinuousRecognitionSession.Completed += onCompleted;


			var webSearchGrammar = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.WebSearch, "webSearch");
			speechRecognizer.Constraints.Add(webSearchGrammar);
			await speechRecognizer.CompileConstraintsAsync();

			await speechRecognizer.RecognizeAsync();
		}
	}
}
