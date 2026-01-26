// Dit is mijn programma.

using System.Diagnostics;
using SeppConsoleApp;

while (true)
{
    SetTitel();

    Console.WriteLine("wat voor som wil je maken,");
    Console.WriteLine("1=+");
    Console.WriteLine("2=-");
    Console.WriteLine("3=x");
    Console.WriteLine("4=:");
    Console.WriteLine("5=\u221a");
    Console.WriteLine("6=\u00b2");

    var soortSom = Console.ReadKey(true);



    double antwoord = 0;
    if (soortSom.KeyChar == '1')
    {
        var getal1 = VulEenGetalIn("eerste");
        if (getal1 == null)
            continue;
        var getal2 = VulEenGetalIn("tweede");
        if (getal2 == null)
            continue;
        SetTitel();
        antwoord = getal1.Value + getal2.Value;
    }
    else if (soortSom.KeyChar == '2')
    {
        var getal1 = VulEenGetalIn("eerste");
        if (getal1 == null)
            continue;
        var getal2 = VulEenGetalIn("tweede");
        if (getal2 == null)
            continue;
        SetTitel();
        antwoord = getal1.Value - getal2.Value;
    }
    else if (soortSom.KeyChar == '3')
    {
        var getal1 = VulEenGetalIn("eerste");
        if (getal1 == null)
            continue;
        var getal2 = VulEenGetalIn("tweede");
        if (getal2 == null)
            continue;
        SetTitel();
        antwoord = getal1.Value * getal2.Value;
    }
    else if (soortSom.KeyChar == '4')
    {
        var getal1 = VulEenGetalIn("eerste");
        if (getal1 == null)
            continue;
        var getal2 = VulEenGetalIn("tweede");
        if (getal2 == null)
            continue;
        SetTitel();
        antwoord = getal1.Value / getal2.Value;
    }
    else if (soortSom.KeyChar == '5')
    {
        var getal1 = VulEenGetalIn("");
        if (getal1 == null)
            continue;
        SetTitel();
        antwoord = Math.Sqrt(getal1.Value);
    }
    else if (soortSom.KeyChar == '6')
    {
        var getal1 = VulEenGetalIn("");
        if (getal1 == null)
            continue;
        SetTitel();
        antwoord = Math.Pow(getal1.Value, 2);
    }
    else
    {
        Console.WriteLine(soortSom.KeyChar + " is niet een mogelijkheid om mee te rekenen op dit moment");
        if (!NogEenKeer())
        {
            break;
        }
        else
        {
            continue;
        }
    }


    Console.WriteLine("het antwoord is " + antwoord.ToString());

    if (!NogEenKeer())
    {
        break;
    }
}

void SetTitel()
{
    Console.Clear();

    var s = "\r\n  ______     _                                   _     _            \r\n  | ___ \\   | |                                 | |   (_)           \r\n  | |_/ /___| | _____ _ __  _ __ ___   __ _  ___| |__  _ _ __   ___ \r\n  |    // _ \\ |/ / _ \\ '_ \\| '_ ` _ \\ / _` |/ __| '_ \\| | '_ \\ / _ \\\r\n  | |\\ \\  __/   <  __/ | | | | | | | | (_| | (__| | | | | | | |  __/\r\n  \\_| \\_\\___|_|\\_\\___|_| |_|_| |_| |_|\\__,_|\\___|_| |_|_|_| |_|\\___|\r\n                                                                    \r\n                                                                    \r\n\r\n";

    Console.WriteLine(s);
    Console.WriteLine();

}

bool NogEenKeer()
{
    Console.WriteLine("wil je nog een som maken? Type j voor ja  n voor nee.");

    var nogEenKeer = Console.ReadKey(true);

    if (nogEenKeer.Key == ConsoleKey.N)
    {
        return false;
    }

    return true;
}

double? VulEenGetalIn(string hoeveelste)
{
    SetTitel();

    var line = "vul het " + hoeveelste + " getal in";

    Console.WriteLine(line.RemoveExtraSpaces());

    if (double.TryParse(Console.ReadLine(), out var getal))
    {
        Console.WriteLine("je hebt " + getal + " ingevuld");
    }
    else
    {

        //wacht vijf seconden op een gebruiker om te reageren, na vijf seconden wordt line 120 uitgevoerd
        var stopwatch = Stopwatch.StartNew();
        
        Console.WriteLine("Dat is geen geldig getal, wil je doorgaan? Type j voor ja n voor nee en b voor naar beginscherm.");
        while (stopwatch.Elapsed.TotalSeconds < 10)
        {
            if (Console.KeyAvailable)
            {
                var antwoord = Console.ReadKey(intercept: true);
                if (antwoord.Key == ConsoleKey.N)
                {
                    Environment.Exit(0);
                }else if (antwoord.Key == ConsoleKey.B)
                {
                    return null; // Terug naar het begin van het programma
                }
                else if (antwoord.Key == ConsoleKey.J)
                {
                    // Gebruiker wil doorgaan, dus we breken de loop af
                    Console.WriteLine("We gaan door met de som.");
                }
                break;
            }

            Thread.Sleep(100); // Avoid busy waiting
        }
        
        // Herhaal de vraag om een getal in te voeren
        return VulEenGetalIn(hoeveelste);
    }

    return getal;

}