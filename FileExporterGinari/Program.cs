using FileExporterGinari;
using FileExporterNew.Models;
using FileExporterNew.Services;
using Prometheus;

public class Program // הפוך את המחלקה ל-public
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));

        builder.Services.AddSingleton<FileHelper>();
        builder.Services.AddSingleton<MetricsManager>();
        builder.Services.AddSingleton<FailureSearchService>();
        builder.Services.AddSingleton<ZombieSearchService>();
        builder.Services.AddSingleton<TranscodedSearchService>();

        builder.Services.AddScoped<ScanManagerService>();

        // הוספת שירות הרקע שיפעיל את הסריקות באופן מחזורי
        builder.Services.AddHostedService<FileScanningWorker>();

        builder.Services.AddControllers();
        // הוספת תמיכה ב-Swagger/OpenAPI לתיעוד ה-API (אופציונלי אבל מומלץ)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // --- 2. בניית האפליקציה ---
        var app = builder.Build();

        // --- 3. הגדרת ה-Pipeline של בקשות ה-HTTP ---

        app.UseSwagger();
        app.UseSwaggerUI();


        // הפניה מ-HTTP ל-HTTPS (אופציונלי)
        app.UseHttpsRedirection();

        // הוספת תמיכה ב-Routing
        app.UseRouting();

        // הגדרת נקודת הקצה של Prometheus
        // כל פעם שמישהו יגש ל- http://<your_server>:8080/metrics, ספריית Prometheus תחשוף את כל המדדים העדכניים.
        app.MapMetrics();

        // מיפוי ה-Controllers (אם ישנם)
        app.MapControllers();


        // --- 4. הפעלת האפליקציה ---
        app.Run();
    }
}