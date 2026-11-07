using VigiaHud.Console;

// dotnet run --project src/VigiaHud.Console -- <comando> [opciones]
// The owner reads what this prints, so it speaks Spanish; the code stays in English.
var output = System.Console.Out;
var error = System.Console.Error;
try
{
    var options = Options.Parse(args);
    return options.Command switch
    {
        "info" => InfoCommand.Run(options, output),
        "hoja" => SheetCommand.Run(options, output),
        "juzgar" => JudgeCommand.Run(options, output),
        _ => Usage(error),
    };
}
catch (UsageException problem)
{
    error.WriteLine(problem.Message);
    return 2;
}

static int Usage(TextWriter error)
{
    error.WriteLine("""
        Uso: dotnet run --project src/VigiaHud.Console -- <comando> [opciones]

          info                  lista las grabaciones: fotos, duración, etiquetas, tamaño y huella
          hoja <grabación>      página con las fotos para mirarlas y etiquetarlas, junto a la grabación
                [--desde mm:ss] [--hasta mm:ss] [--cada N]   un rato concreto, o una foto cada N segundos

          juzgar                pasa el detector por las grabaciones etiquetadas: detectadas, perdidas y falsas alarmas
                [--umbral 20] [--area 1] [--luz 60] [--seguidos 2] [--reducir 2] [--referencia anterior|fondo]
                [--aprendizaje 5] [--cada 1] [--cooldown 120] [--adelanto 5]
                (área, luz y aprendizaje en %; cada, cooldown y adelanto en segundos)

          --carpeta <ruta>      dónde están las grabaciones (por defecto %USERPROFILE%\.vigiahud\secuencias)

        <grabación> es el fichero .vseq o un trozo de su nombre (por ejemplo salida-1).
        """);
    return 2;
}
