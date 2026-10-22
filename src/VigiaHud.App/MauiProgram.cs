using Microsoft.Extensions.Logging;
using VigiaHud.Core.Logging;

namespace VigiaHud.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("ChakraPetch-Regular.ttf", "ChakraPetch");
                fonts.AddFont("ChakraPetch-Bold.ttf", "ChakraPetchBold");
            });

        // The watch log (DECISIONS 006): the judge of phase 1, in the app's private files
        // (/data/user/0/com.vigiahud.app/files/logs; "adb shell run-as com.vigiahud.app cat files/logs/vigia.log").
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new RotatingFileLogOptions(Path.Combine(FileSystem.AppDataDirectory, "logs")));
        builder.Services.AddSingleton<IWatchLog>(sp =>
            new RotatingFileLog(sp.GetRequiredService<RotatingFileLogOptions>(), sp.GetRequiredService<TimeProvider>()));

        // The watch itself: a foreground service on Android (step 3) that captures on Core's timings (step 4).
        builder.Services.AddSingleton(VigiaHud.Core.Watch.WatchTimings.Default);
#if ANDROID
        builder.Services.AddSingleton<Watch.IWatchController, Platforms.Android.Watch.AndroidWatchController>();
        builder.Services.AddSingleton<VigiaHud.Core.Device.IDeviceStatusSource, Platforms.Android.Device.AndroidDeviceStatusSource>();
#endif

        builder.Services.AddTransient<WatchPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
