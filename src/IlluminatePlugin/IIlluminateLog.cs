using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yarukizero.Net.IlluminateSpeech {
	public interface IIlluminateLog {
		public void Put(string message);
		public void Put(Exception ex);

		public void Debug(string message);
		public void Debug(Exception ex);
	}
}
