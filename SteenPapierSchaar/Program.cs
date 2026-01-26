using Microsoft.Extensions.DependencyInjection;

public interface IGame
{
    void Play();
}

public class RockPaperScissorsGame(IScoreboard scoreboard, IPlayer player1, IPlayer player2) : IGame
{
    public void Play()
    {
        Console.WriteLine("Hoeveel rondes wil je spelen?");
        int rounds = int.Parse(Console.ReadLine() ?? "1");

        for (int i = 0; i < rounds; i++)
        {
            var player1Move = player1.GetMove();
            var player2Move = player2.GetMove();
            Console.WriteLine($"PLayer1 kiest: {player1Move}");
            Console.WriteLine($"PLayer2 kiest: {player2Move}");

            var result = GetResult(player1Move, player2Move);
            scoreboard.Update(result);
            Console.WriteLine($"Resultaat: {result}\n");
        }

        scoreboard.Show();
    }

    private string GetResult(string player1, string player2)
    {
        if (player1 == player2)
            return "Draw";

        if ((player1 == "rock" && player2 == "scissors") ||
            (player1 == "paper" && player2 == "rock") ||
            (player1 == "scissors" && player2 == "paper"))
            return "Player1 wins";

        return "Player2 wins";
    }

}

public interface IPlayer
{
    string GetMove();
}

public class HumanPlayer : IPlayer
{
    public string GetMove()
    {
        Console.WriteLine("Choose: rock (r), paper (p), or scissors (s)");

        string input = ReadHiddenInput();
        return input switch
        {
            "r" => "rock",
            "p" => "paper",
            "s" => "scissors",
            _ => "rock" // fallback
        };
    }

    private static string ReadHiddenInput()
    {
        Console.Write("Your choice (hidden): ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();
        return key.KeyChar.ToString().ToLower();
    }
}


public class ComputerPlayer : IPlayer
{
    private static readonly string[] Moves = { "rock", "paper", "scissors" };
    private readonly Random _random = new();

    public string GetMove()
    {
        return Moves[_random.Next(Moves.Length)];
    }
}


public interface IScoreboard
{
    void Update(string result);
    void Show();
}

public class Scoreboard : IScoreboard
{
    private int _player1Wins;
    private int _player2Wins;
    private int _draws;

    public void Update(string result)
    {
        switch (result)
        {
            case "Player1 wins": _player1Wins++; break;
            case "Player2 wins": _player2Wins++; break;
            default: _draws++; break;
        }
    }

    public void Show()
    {
        Console.WriteLine("Final Score:");
        Console.WriteLine($"Player1: {_player1Wins}");
        Console.WriteLine($"Player2: {_player2Wins}");
        Console.WriteLine($"Draws: {_draws}");
    }
}


class Program
{
    static IPlayer ChoosePlayer(IServiceProvider provider, string prompt)
    {
        Console.WriteLine($"{prompt} (h)uman of (c)omputer?");
        var choice = Console.ReadLine()?.ToLower();

        return choice switch
        {
            "h" => provider.GetRequiredService<HumanPlayer>(),
            "c" => provider.GetRequiredService<ComputerPlayer>(),
            _ => throw new InvalidOperationException("Ongeldige keuze")
        };
    }
    static void ShowSplashScreen()
    {
        Console.Clear();
        Console.WriteLine(@"
                  :#*+*                                 .%**********@:.                      -=       --     
                #-------*.                           ...-+         .#:#..                    @@:     .@@     
              *+----------#                       :=##*:..+         .#..-*..                 .@@:   :@@.     
            :*=+*----------:                      :*.    .+              .#.                  -@@   @@-      
          =*====+#=--------*                       *.    .+              .#.                   +@@.@@*       
       -#+========+*--------*                      :+    .+              .#.                    %@@ #        
      **++=========#=-------+:                     .%:   .+              .#.                     @ @         
     +  .#=========++-=+*****+                      :+   .+              .#.                    # @@@        
    =:    #=========#:....:-*:                       +.  .+              .#.              #@@# %*   +@ *@@#  
     +    .#========*-.....*                         :#  .+              .#.          .%    @=     =@    %:  
       #*  .#========*=...*                          .#: .+              .#.          .%    @.      @    %:  
           =+%++++++++***                             .+ .%***************#.            *@@*         +@@#   
                                                      .#.    .+*#+-..                                      
                                                       :@%=-..                                             
             STEEN                                            PAPIER                          SCHAAR
");

        Console.WriteLine("\nWelkom bij Steen, Papier, Schaar!");
        Console.WriteLine("Druk op een toets om te beginnen...");
        Console.ReadKey();
    }

    static void Main(string[] args)
    {
        ShowSplashScreen();

        var services = new ServiceCollection();
        services.AddSingleton<HumanPlayer>();
        services.AddSingleton<ComputerPlayer>();
        services.AddSingleton<IScoreboard, Scoreboard>();
 
        var provider = services.BuildServiceProvider();
        var player1 = ChoosePlayer(provider, "Kies speler 1");
        var player2 = ChoosePlayer(provider, "Kies speler 2");
        var scoreboard = provider.GetRequiredService<IScoreboard>();

        var game = new RockPaperScissorsGame(scoreboard, player1, player2);
        game.Play();
    }
}
