using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Yarukizero.Net.IlluminateSpeech.Windows {
	/// <summary>
	/// MainWindow.xaml の相互作用ロジック
	/// </summary>
	public partial class MainWindow : Window {
		private System.Reactive.Concurrency.EventLoopScheduler SpeechScheduler { get; } = new System.Reactive.Concurrency.EventLoopScheduler();
		private IIlluminateRecognition? recognition;
		private Helpers.TTSPluginWrapper tts = new Helpers.TTSPluginWrapper(new IlluminateSpeech.TextToSpeech.FileExport.Plugin());
		private System.Threading.CancellationTokenSource? cancellationTokenSource;

		public MainWindow() {
			InitializeComponent();
			this.Loaded += (_, _) => {
				this.tts.Load(App.Instance.Context);
			};
			this.Closing += (_, _) => {
				this.cancellationTokenSource?.Cancel();
			};
		}

		private void OnClick(object sender, RoutedEventArgs e) {
			if(this.recognition == null) {
				this.recognition = new Recognitions.ChromiumRecognition.Plugin();
				//this.recognition = new Recognitions.WinRTRecognition.Plugin();
			}
			if(cancellationTokenSource == null) {
				this.cancellationTokenSource = new System.Threading.CancellationTokenSource();
				tts.StartPlugin();
				this.recognition.InitPlugin();
				this.recognition.Run(this.cancellationTokenSource.Token)
					.ObserveOn(this.SpeechScheduler)
					.Subscribe(x => {
						var text = x?.Transcript?.Trim() ?? "";
						if(!string.IsNullOrEmpty(text)) {
							var fs = x?.IsFinish ?? false;
							this.tts.DoBeforeSpeech(text, fs);
							this.tts.Speak(text, fs);
							this.tts.DoAfterSpeech(text, fs);
						}
					});
			} else {
				tts.EndPlugin();
				this.recognition.DeinitPlugin();
				this.cancellationTokenSource.Cancel();
				this.cancellationTokenSource = null;
			}
		}
	}
}
