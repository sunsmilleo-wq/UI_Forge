using System.Text;
using System.Windows;

namespace UiForge.App
{
    public partial class App : Application
    {
        public App()
        {
            // Нужно для Encoding.GetEncoding(936) (GBK) в TextCodec — без явной
            // регистрации провайдера .NET не знает ни одной кодовой страницы,
            // кроме Unicode-семейства, и будет молча падать в try/catch.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
