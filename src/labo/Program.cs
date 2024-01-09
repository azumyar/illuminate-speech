using System.Reactive.Linq;
using System.Diagnostics;
using System.Net;
using System.Text;

const string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
const int httpPort = 12345;
const int wsPort = 23456;

string html = @$"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <title>るみすぴ</title>
</head>
<body>
<p>しゃべって</p>
<p id='recognition'></p>
<script>
  const el = document.querySelector('#recognition');
  let con = new WebSocket('ws://localhost:{wsPort}');

  SpeechRecognition = webkitSpeechRecognition || SpeechRecognition;
  const recognition = new SpeechRecognition();
  recognition.lang = 'ja-JP';
  recognition.continuous = true;
  recognition.interimResults = true;
  recognition.onresult = (event) => {{
    let transcript = '';
    let fin = false;
    for(let i = event.resultIndex; i<event.results.length; i++) {{
      transcript += event.results[i][0].transcript;
      fin |= event.results[i].isFinal;
    }}
    el.innerText = fin ? '' : transcript;
    /*
    if((con == null) || (con.readyState != 1)) {{
      con = new WebSocket('ws://localhost:{wsPort}');
    }}
    */
    con.send(JSON.stringify({{ transcript, fin }}));
  }}
  recognition.start();
</script>
</body>
</html>
";

var cancel = new CancellationTokenSource();

using var httpSubscriber = Observable.Create<int>(async o => {
	try {
		var listener = new HttpListener();
		listener.Prefixes.Clear();
		listener.Prefixes.Add($"http://localhost:{httpPort}/");
		listener.Start();

		while(!cancel.Token.IsCancellationRequested) {
			var context = await listener.GetContextAsync();
			var request = context.Request;
			var response = context.Response;
			try {
				// リクエストの詳細は無視する
				if(request != null) {
					var text = Encoding.UTF8.GetBytes(html);
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
	catch(Exception e) {
		Console.Error.WriteLine(e);
	}
	return System.Reactive.Disposables.Disposable.Empty;
}).SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default)
	.Subscribe();

using var wsSubscriber = Observable.Create<Dictionary<string, object>>(o => {
	try {
		using var server = new Fleck.WebSocketServer($"ws://127.0.0.1:{wsPort}");

		server.Start(socket => {
			socket.OnOpen = () => {
				Console.WriteLine("connect");
			};
			socket.OnClose = () => { };
			socket.OnMessage = message => {
				try {
					var json = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
					if(json != null) {
						o.OnNext(json);
					}
				}
				catch(Exception e) {
					Console.Error.WriteLine(e);
				}
			};
		});
		cancel.Token.WaitHandle.WaitOne();
	}
	finally {
		o.OnCompleted();
	}
	return System.Reactive.Disposables.Disposable.Empty;
}).SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default)
	.Subscribe(x => {
		if(x["fin"] is long b && b != 0) {
			Console.WriteLine($"認識=> {x["transcript"]}");
		}
	});

using var p = Process.Start(new ProcessStartInfo() {
	FileName = chrome,
	Arguments = $"--disable-extensions -app=http://localhost:{httpPort}/ --unsafely-treat-insecure-origin-as-secure=http://localhost:{httpPort}/",
	CreateNoWindow = true,
	UseShellExecute = false,
});


Console.WriteLine("エンター押下で終了");
Console.ReadLine();
cancel.Cancel();
// chromeは自分で閉じる








