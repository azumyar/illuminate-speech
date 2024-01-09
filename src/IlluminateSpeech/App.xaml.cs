using Newtonsoft.Json;
using Prism;
using Prism.Ioc;
using Prism.Unity;
using Reactive.Bindings;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Yarukizero.Net.IlluminateSpeech {
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : PrismApplication {
		private string log4config = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<configuration>
  <configSections>
    <section name=""log4net"" type=""log4net.Config.Log4NetConfigurationSectionHandler,log4net"" />
  </configSections>

  <log4net>
    <!-- とりあコンソール -->
    <appender name=""LogToConsole"" type=""log4net.Appender.ConsoleAppender"">
      <layout type=""log4net.Layout.PatternLayout"">
        <conversionPattern value=""%d[%t] %p - %m%n""/>
      </layout>
    </appender>

    <root>
      <level value=""Warn"" />
      <appender-ref ref=""LogToConsole"" />
    </root>
  </log4net>
</configuration>";
		private readonly log4net.ILog logger;
#pragma warning disable CS8618
		public static App Instance { get; private set; }
#pragma warning restore


		public IIlluminateContext Context {get; private set;}

		public App() {
			Instance = this;
			log4net.Config.XmlConfigurator.Configure(new MemoryStream(Encoding.UTF8.GetBytes(log4config)));
			logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);
			Yukarinette.YukarinetteLogger.Instance.SetLogger(logger);

			this.Context = new Models.IlluminateContext(logger);
		}

		protected override Window CreateShell() {
			return new Windows.MainWindow();
		}

		protected override void OnStartup(StartupEventArgs e) {
			base.OnStartup(e);

			UIDispatcherScheduler.Initialize();
			var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IlluminateSpeech");
			if(!Directory.Exists(appData) ) {
				Directory.CreateDirectory(appData);
			}
			if(!Directory.Exists(this.Context.PluginConfigDirectory)) {
				Directory.CreateDirectory(this.Context.PluginConfigDirectory);
			}

			Helpers.PluginLoader.Instance.LoadTTSPlugin("");
			if(Helpers.PluginLoader.Instance.Plugins.IsYukarinetteContained) {
				if(!Directory.Exists(Yukarinette.YukarinetteCommon.AppSettingFolder)) {
					Directory.CreateDirectory(Yukarinette.YukarinetteCommon.AppSettingFolder);
					Directory.CreateDirectory(Path.Combine(Yukarinette.YukarinetteCommon.AppSettingFolder, "plugins"));
				}
			}
		}


		protected override void RegisterTypes(IContainerRegistry containerRegistry) {}
	}
}
