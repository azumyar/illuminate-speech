using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yarukizero.Net.IlluminateSpeech.TextToSpeech.FileExport; 

public class Plugin : IlluminatePlugin {
	public string exportDir;
	public string exportPath;
	public DateTime token = DateTime.MinValue;

	public Plugin() {
		this.exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IlluminateSpeech", "plugin");
		this.exportPath = Path.Combine(this.exportDir, "Yarukizero.Net.FileExport.Export.txt");
	}

	public override void StartPlugin() {
		File.WriteAllText(this.exportPath, "");
	}

	public override void Speak(string text, bool isFinish) {
		if(isFinish) {
			this.token = DateTime.Now;
			File.WriteAllText(this.exportPath, text);
			Observable.Return(this.token)
				.Delay(TimeSpan.FromSeconds(5))
				.Subscribe(x => {
					if(x == this.token) {
						File.WriteAllText(this.exportPath, "");
					}
				});
		}
	}
}
