using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using Reactive.Bindings;

namespace Yarukizero.Net.IlluminateSpeech.Helpers {
	internal class PluginLoader {

		public static PluginLoader Instance { get; } = new PluginLoader();

		public (IEnumerable<TTSPluginWrapper> Plugins, bool IsYukarinetteContained) Plugins { get; private set; } = (Enumerable.Empty<TTSPluginWrapper>(), false);

		private PluginLoader() { }

		public void LoadTTSPlugin(string path) {
			if(!Directory.Exists(path)) {
				return;
			}

			var r = new List<TTSPluginWrapper>();
			var yukarinette = false;
			foreach(var f in Directory.GetFiles(path, "*.dll")) {
				try {
					var asm = Assembly.LoadFile(f);
					var p = asm.ExportedTypes
						.Where(x => x.IsSubclassOf(typeof(Yukarinette.IYukarinetteInterface)) && (x.GetCustomAttribute<IgnoreIlluminatePluginAttribute>() == null))
						.Cast<Yukarinette.IYukarinetteInterface>()
						.Select(x => new TTSPluginWrapper(x));
					yukarinette |= p.Any();
					r.AddRange(p);
					r.AddRange(asm.ExportedTypes
						.Where(x => x.IsSubclassOf(typeof(IlluminatePlugin)))
						.Cast<IlluminatePlugin>()
						.Select(x => new TTSPluginWrapper(x)));
				}
				catch(Exception) { }
			}
			this.Plugins = (r.AsReadOnly(), yukarinette);
		}

		public void Unload() {
			foreach(var p in this.Plugins.Plugins) {
				p.Dispose();
			}
			this.Plugins = (Enumerable.Empty<TTSPluginWrapper>(), false);
		}
	}
}
