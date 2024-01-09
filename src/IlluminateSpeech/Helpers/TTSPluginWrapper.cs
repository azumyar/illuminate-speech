using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Yarukizero.Net.IlluminateSpeech.Helpers {
	internal class TTSPluginWrapper : IDisposable {
		private Yukarinette.IYukarinetteInterface? yukarinette;
		private IlluminatePlugin? illuminate;


		public TTSPluginWrapper(Yukarinette.IYukarinetteInterface yukarinette) { 
			this.yukarinette = yukarinette;
		}

		public TTSPluginWrapper(IlluminatePlugin illuminate) {
			this.illuminate = illuminate;
		}

		public void Dispose() {
			this.yukarinette?.Closed();
			this.illuminate?.Dispose();
		}

		public string Id {
			get {
				string asm;
				string type;
				if(this.yukarinette != null) {
					asm = Path.GetFileNameWithoutExtension(this.yukarinette.GetType().Assembly.Location);
					type = this.yukarinette.GetType().FullName ?? "";
				} else if(this.illuminate != null) {
					asm = Path.GetFileNameWithoutExtension(this.illuminate.GetType().Assembly.Location);
					type = this.illuminate.GetType().FullName ?? "";
				} else {
					throw new Exception();
				}
				return $"{asm}::{type}";
			}
		}


		public void Load(IIlluminateContext context) {
			try {
				this.yukarinette?.Loaded();
				this.illuminate?.Load(context);
			}
			catch(Yukarinette.YukarinetteException) { }
		}

		public void Setting() {
		}

		public void StartPlugin() {
			try {
				this.yukarinette?.SpeechRecognitionStart();
				this.illuminate?.StartPlugin();
			}
			catch(Yukarinette.YukarinetteException) { }
		}

		public void EndPlugin() {
			this.yukarinette?.SpeechRecognitionStop();
			try {
				this.yukarinette?.SpeechRecognitionStop();
				this.illuminate?.EndPlugin();
			}
			catch(Yukarinette.YukarinetteException) { }
		}

		public void DoBeforeSpeech(string text, bool isFinish) {
			try {
				this.yukarinette?.BeforeSpeech(text);
				this.illuminate?.DoBeforeSpeech(text, isFinish);
			}
			catch(Yukarinette.YukarinetteException) { }
		}

		public void Speak(string text, bool isFinish) {
			try {
				this.yukarinette?.Speech(text);
				this.illuminate?.Speak(text, isFinish);
			}
			catch(Yukarinette.YukarinetteException) { }
		}

		public void DoAfterSpeech(string text, bool isFinish) {
			try {
				this.yukarinette?.AfterSpeech(text);
				this.illuminate?.DoAfterSpeech(text, isFinish);
			}
			catch(Yukarinette.YukarinetteException) { }
		}


	}
}
