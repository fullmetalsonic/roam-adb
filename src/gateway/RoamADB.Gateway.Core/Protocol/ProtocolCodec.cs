using System.Buffers;
using System.Text.Json;

namespace RoamADB.Gateway.Protocol;

public static class ProtocolCodec
{
  public const int CurrentVersion = 1;
  public const int MaximumMessageBytes = 65_536;

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    PropertyNameCaseInsensitive = false,
    WriteIndented = false
  };

  public static async ValueTask<WireMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken)
  {
    var writer = new ArrayBufferWriter<byte>();
    var singleByte = new byte[1];

    while (writer.WrittenCount < MaximumMessageBytes)
    {
      var read = await stream.ReadAsync(singleByte, cancellationToken).ConfigureAwait(false);
      if (read == 0)
      {
        return writer.WrittenCount == 0
          ? null
          : throw new InvalidDataException("The connection ended inside a protocol message.");
      }

      if (singleByte[0] == (byte)'\n')
      {
        return JsonSerializer.Deserialize<WireMessage>(writer.WrittenSpan, JsonOptions)
          ?? throw new InvalidDataException("The protocol message was empty.");
      }

      writer.GetSpan(1)[0] = singleByte[0];
      writer.Advance(1);
    }

    throw new InvalidDataException($"A protocol message exceeded {MaximumMessageBytes} bytes.");
  }

  public static async ValueTask WriteAsync(
    Stream stream,
    WireMessage message,
    CancellationToken cancellationToken)
  {
    var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
    if (payload.Length + 1 > MaximumMessageBytes)
    {
      throw new InvalidDataException($"A protocol message exceeded {MaximumMessageBytes} bytes.");
    }

    await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
  }
}
