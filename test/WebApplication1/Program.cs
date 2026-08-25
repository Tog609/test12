using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var viewers = new ConcurrentBag<WebSocket>();

// Страница для друга (захват экрана)
app.MapGet("/", () =>
    """
    <html>
    <body>
        <h1>Трансляция экрана</h1>
        <button onclick="start()">Начать трансляцию</button>

        <script>
            let ws;

            async function start() {
                ws = new WebSocket("wss://" + location.host + "/ws");

                ws.onopen = async () => {
                    const stream = await navigator.mediaDevices.getDisplayMedia({ video: true });
                    const video = document.createElement('video');
                    video.srcObject = stream;
                    await video.play();

                    const canvas = document.createElement('canvas');
                    const ctx = canvas.getContext('2d');

                    function sendFrame() {
                        canvas.width = video.videoWidth;
                        canvas.height = video.videoHeight;
                        ctx.drawImage(video, 0, 0);

                        const data = canvas.toDataURL("image/jpeg", 0.5);
                        ws.send(data);

                        requestAnimationFrame(sendFrame);
                    }

                    sendFrame();
                };
            }
        </script>
    </body>
    </html>
    """
);

// Страница для тебя (просмотр)
app.MapGet("/viewer", () =>
    """
    <html>
    <body>
        <h1>Просмотр видеопотока</h1>
        <img id="video" style="width: 90%; border: 1px solid black;" />

        <script>
            const ws = new WebSocket("wss://" + location.host + "/ws-viewer");

            ws.onmessage = (msg) => {
                document.getElementById("video").src = msg.data;
            };
        </script>
    </body>
    </html>
    """
);

// Друг → сервер
app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return;

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    var buffer = new byte[1024 * 1024];

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.CloseStatus.HasValue)
            break;

        var base64 = Encoding.UTF8.GetString(buffer, 0, result.Count);

        foreach (var viewer in viewers.ToArray())
        {
            if (viewer.State == WebSocketState.Open)
            {
                await viewer.SendAsync(
                    Encoding.UTF8.GetBytes(base64),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
        }
    }
});

// Ты → сервер
app.Map("/ws-viewer", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return;

    var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    viewers.Add(socket);

    var buffer = new byte[1];

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.CloseStatus.HasValue)
            break;
    }
});

app.Run();
