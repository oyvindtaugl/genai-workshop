using Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Diagnostics;

// Simple CLI entry.
// Usage examples:
// dotnet run --project Cli -- resetdb
// dotnet run --project Cli -- updatedb
// dotnet run --project Cli

if (args.Length >0)
{
    var command = args[0];
    if (string.Equals(command, "resetdb", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            AnsiConsole.MarkupLine("[cyan]resetdb[/]: adding missing migrations, dropping and recreating [yellow]ApplicationDbContext[/] database using EF Core...");

            var configuration = BuildConfiguration();
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                AnsiConsole.MarkupLine("[red]Connection string 'DefaultConnection' not found.[/]");
                return;
            }

            // Try to add a migration if there are model changes.
            // We invoke 'dotnet ef migrations add' on the Authentication project.
            var authProjectPathCandidates = new[]
            {
                Path.Combine("..", "Authentication"),
                Path.Combine("../../Authentication"),
                "Authentication"
            };
            var authProjectDir = authProjectPathCandidates.FirstOrDefault(Directory.Exists);
            if (authProjectDir != null)
            {
                var migrationName = $"Auto_{DateTime.UtcNow:yyyyMMddHHmmss}";
                var addArgs = $"ef migrations add {migrationName} --project \"{authProjectDir}\" --context ApplicationDbContext --output-dir Migrations";
                var exitAdd = RunDotNet(addArgs);
                if (exitAdd ==0)
                {
                    AnsiConsole.MarkupLine($"[grey]Migration attempt completed: {migrationName}[/]");
                    // Rebuild to ensure newly added migration is compiled before applying.
                    var buildArgs = $"build \"{authProjectDir}\"";
                    if (RunDotNet(buildArgs) !=0)
                    {
                        AnsiConsole.MarkupLine("[red]Build failed after adding migration. Aborting reset.[/]");
                        return;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]No new migration added (or add failed). Proceeding with reset.[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Authentication project directory not found. Skipping migration add step.[/]");
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            using (var context = new ApplicationDbContext(options))
            {
                AnsiConsole.MarkupLine("[grey]Ensuring database is deleted...[/]");
                context.Database.EnsureDeleted();

                AnsiConsole.MarkupLine("[grey]Applying migrations...[/]");
                context.Database.Migrate();

                AnsiConsole.MarkupLine("[green]Database reset complete.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]resetdb failed:[/] {Markup.Escape(ex.Message)}");
        }
        return;
    }
    else if (string.Equals(command, "updatedb", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            AnsiConsole.MarkupLine("[cyan]updatedb[/]: applying pending EF Core migrations to [yellow]ApplicationDbContext[/]...");

            var configuration = BuildConfiguration();
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                AnsiConsole.MarkupLine("[red]Connection string 'DefaultConnection' not found.[/]");
                return;
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            using (var context = new ApplicationDbContext(options))
            {
                var pending = context.Database.GetPendingMigrations().ToList();
                if (pending.Count ==0)
                {
                    AnsiConsole.MarkupLine("[yellow]No pending migrations.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[grey]{pending.Count} pending migration(s):[/]");
                    foreach (var m in pending)
                        AnsiConsole.MarkupLine($" - [blue]{Markup.Escape(m)}[/]");

                    AnsiConsole.MarkupLine("[grey]Applying migrations...[/]");
                    context.Database.Migrate();
                    AnsiConsole.MarkupLine("[green]Migrations applied successfully.[/]");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]updatedb failed:[/] {Markup.Escape(ex.Message)}");
        }
        return;
    }
}

AnsiConsole.MarkupLine("[yellow]TBD CLI[/] (no command specified). Try: [bold]resetdb[/] or [bold]updatedb[/]");

static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddJsonFile(Path.Combine("..", "Authentication", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine("..", "Authentication", "appsettings.Development.json"), optional: true)
    .Build();

static int RunDotNet(string arguments)
{
    AnsiConsole.MarkupLine($"> [bold]dotnet {Markup.Escape(arguments)}[/]");
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var proc = Process.Start(psi);
    if (proc == null)
        return -1;
    proc.OutputDataReceived += (_, e) => { if (e.Data != null) AnsiConsole.MarkupLine(Markup.Escape(e.Data)); };
    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) AnsiConsole.MarkupLine("[red]" + Markup.Escape(e.Data) + "[/]"); };
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    proc.WaitForExit();
    return proc.ExitCode;
}
