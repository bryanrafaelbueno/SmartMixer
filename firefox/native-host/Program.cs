using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

const int MaximumMessageBytes = 1024 * 1024;
await using var pipe = new NamedPipeClientStream(".", "SmartMixerFirefox", PipeDirection.InOut, PipeOptions.Asynchronous);
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
var outputLock = new SemaphoreSlim(1, 1);

while (true)
{
    try
    {
        await pipe.ConnectAsync(1000);
        break;
    }
    catch (TimeoutException)
    {
        await Task.Delay(500);
    }
}

using var appReader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
using var appWriter = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };

var commandPump = Task.Run(async () =>
{
    while (pipe.IsConnected)
    {
        var line = await appReader.ReadLineAsync();
        if (line is null) break;
        await WriteNativeMessageAsync(output, outputLock, JsonDocument.Parse(line).RootElement.Clone());
    }
});

while (true)
{
    var message = await ReadNativeMessageAsync(input);
    if (message is null) break;
    await appWriter.WriteLineAsync(message.Value.GetRawText());
    await WriteNativeMessageAsync(output, outputLock, JsonDocument.Parse("{\"received\":true}").RootElement);
}

try { await commandPump; }
catch (IOException) { }
return 0;

async Task<JsonElement?> ReadNativeMessageAsync(Stream input)
{
    var lengthBytes = new byte[4];
    var first = await input.ReadAsync(lengthBytes.AsMemory(0, 1));
    if (first == 0) return null;
    await input.ReadExactlyAsync(lengthBytes.AsMemory(1, 3));
    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
    if (length is <= 0 or > MaximumMessageBytes)
        throw new InvalidDataException($"Native messaging frame length is invalid: {length}.");
    var payload = new byte[length];
    await input.ReadExactlyAsync(payload);
    using var document = JsonDocument.Parse(payload);
    return document.RootElement.Clone();
}

async Task WriteNativeMessageAsync(Stream output, SemaphoreSlim outputLock, JsonElement message)
{
    var payload = JsonSerializer.SerializeToUtf8Bytes(message);
    if (payload.Length > MaximumMessageBytes)
        throw new InvalidDataException("Native messaging response is larger than the 1 MiB protocol limit.");
    await outputLock.WaitAsync();
    try
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await output.WriteAsync(length);
        await output.WriteAsync(payload);
        await output.FlushAsync();
    }
    finally
    {
        outputLock.Release();
    }
}
