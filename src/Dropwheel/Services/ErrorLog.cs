using System.IO;

namespace Dropwheel.Services;

/// <summary>Пишет ошибки в error.log рядом с config.json. Нужен, чтобы проблемы не
/// пропадали молча: глобальный перехватчик, битые правила и неудачная регистрация
/// горячей клавиши оставляют здесь след. Сам логер никогда не бросает исключений.</summary>
public static class ErrorLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(TargetStore.Dir, "error.log");

    /// <summary>Дописывает строку с меткой времени. Ошибки записи проглатываются
    /// намеренно — логирование не должно рушить приложение.</summary>
    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(TargetStore.Dir);
            var line = ex == null
                ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}"
                : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}: {ex.GetType().Name}: {ex.Message}";
            lock (Gate) File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch { /* некуда писать — это не критично */ }
    }
}
