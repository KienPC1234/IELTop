using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IELTop_Content_Server;

/// <summary>
/// Load module Center (proprietary) bằng reflection nếu có.
/// Khi không có file Center/, build vẫn xanh và chạy ổn định ở chế độ open source.
/// </summary>
public static class CenterModuleLoader
{
    private static bool _loaded;
    private static Type? _bootstrapperType;

    public static bool IsCenterModuleLoaded => _loaded;

    static CenterModuleLoader()
    {
        // Tìm CenterBootstrapper trong assembly hiện tại (nếu Center/ được compile vào).
        _bootstrapperType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.FullName == "IELTop_Content_Server.Center.CenterBootstrapper");

        _loaded = _bootstrapperType is not null;
    }

    /// <summary>
    /// Gọi CenterBootstrapper.ConfigureServices(services) nếu module tồn tại.
    /// </summary>
    public static void ConfigureCenterServices(IServiceCollection services)
    {
        if (_bootstrapperType is null) return;

        var method = _bootstrapperType.GetMethod(
            "ConfigureServices",
            BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(IServiceCollection) });

        method?.Invoke(null, new object[] { services });
    }

    /// <summary>
    /// Gọi CenterBootstrapper.MapEndpoints(app) nếu module tồn tại.
    /// </summary>
    public static void MapCenterEndpoints(WebApplication app)
    {
        if (_bootstrapperType is null) return;

        var method = _bootstrapperType.GetMethod(
            "MapEndpoints",
            BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(WebApplication) });

        method?.Invoke(null, new object[] { app });
    }
}
