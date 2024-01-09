using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yarukizero.Net.IlluminateSpeech.Models {
	internal class IlluminateContext : IIlluminateContext {
		private class Log : IIlluminateLog { 
			private ILog log;

			public Log(ILog logger) {
				this.log = logger;
			}

			public void Put(string message) => this.log.Info(message);
			public void Put(Exception ex) => this.log.Info(ex);
			public void Debug(string message) => this.log.Debug(message);
			public void Debug(Exception ex) => this.log.Debug(ex);
		}

		public IIlluminateLog Logger { get; }

		public IlluminateContext(ILog logger) {
			this.Logger = new Log(logger);
		}
	}
}
