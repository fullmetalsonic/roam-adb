using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace RoamADB.Gateway.Desktop.Services;

public static class QrImageService
{
  public static BitmapImage Create(string payload)
  {
    var png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.Q, 12);
    using var stream = new MemoryStream(png);
    var image = new BitmapImage();
    image.BeginInit();
    image.CacheOption = BitmapCacheOption.OnLoad;
    image.StreamSource = stream;
    image.EndInit();
    image.Freeze();
    return image;
  }
}
