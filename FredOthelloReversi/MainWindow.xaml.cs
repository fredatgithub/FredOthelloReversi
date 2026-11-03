using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace FredOthelloReversi
{
  public partial class MainWindow: Window
  {
    private const int BoardSize = 8;
    private const int Human = 1;
    private const int Computer = -1;
    private readonly string _logFilePath = CreateLogFilePath();
    private static readonly (int Row, int Column)[] Directions =
    [
      (-1, -1), (-1, 0), (-1, 1),
      (0, -1),             (0, 1),
      (1, -1),  (1, 0),   (1, 1)
    ];
    private static readonly int[,] PositionValues =
    {
      { 100, -20, 10, 5, 5, 10, -20, 100 },
      { -20, -40, -5, -5, -5, -5, -40, -20 },
      { 10, -5, 5, 2, 2, 5, -5, 10 },
      { 5, -5, 2, 1, 1, 2, -5, 5 },
      { 5, -5, 2, 1, 1, 2, -5, 5 },
      { 10, -5, 5, 2, 2, 5, -5, 10 },
      { -20, -40, -5, -5, -5, -5, -40, -20 },
      { 100, -20, 10, 5, 5, 10, -20, 100 }
    };

    private readonly int[,] _board = new int[BoardSize, BoardSize];
    private readonly Button[,] _squares = new Button[BoardSize, BoardSize];
    private readonly DateTime _sessionStarted = DateTime.Now;
    private bool _logWriteErrorShown;
    private int _currentPlayer = Human;
    private int _moveCount;
    private int _gameVersion;
    private bool _gameOver;
    private bool _computerThinking;
    private bool _restoreMaximized;

    public MainWindow()
    {
      InitializeComponent();
      RestoreWindowBounds();
      BuildBoard();
      AddLogEntry("Démarrage de l'application.");
      StartNewGame();
    }

    private static string WindowSettingsPath =>
      IOPath.Combine(AppContext.BaseDirectory, "window.json");

    private void RestoreWindowBounds()
    {
      var settingsPath = WindowSettingsPath;
      if (!File.Exists(settingsPath))
        return;

      WindowBounds? bounds;
      try
      {
        bounds = JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(settingsPath));
      }
      catch (JsonException)
      {
        MessageBox.Show(
          "Les dimensions enregistrées de la fenêtre sont illisibles. Les valeurs par défaut seront utilisées.",
          "Othello | Reversi",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }
      catch (IOException exception)
      {
        MessageBox.Show(
          $"Impossible de lire les dimensions enregistrées de la fenêtre : {exception.Message}",
          "Othello | Reversi",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }
      catch (UnauthorizedAccessException exception)
      {
        MessageBox.Show(
          $"Impossible d'accéder aux dimensions enregistrées de la fenêtre : {exception.Message}",
          "Othello | Reversi",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }

      if (bounds is null || !IsUsableWindowBounds(bounds))
        return;

      WindowStartupLocation = WindowStartupLocation.Manual;
      Left = bounds.Left;
      Top = bounds.Top;
      Width = bounds.Width;
      Height = bounds.Height;
      _restoreMaximized = bounds.IsMaximized;
    }

    private bool IsUsableWindowBounds(WindowBounds bounds)
    {
      if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)
        || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
        || bounds.Width < MinWidth || bounds.Height < MinHeight)
        return false;

      var right = bounds.Left + bounds.Width;
      var bottom = bounds.Top + bounds.Height;
      var virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
      var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
      return double.IsFinite(right) && double.IsFinite(bottom)
        && bounds.Left < virtualRight - 80
        && right > SystemParameters.VirtualScreenLeft + 80
        && bounds.Top < virtualBottom - 30
        && bounds.Top + 40 > SystemParameters.VirtualScreenTop;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      if (_restoreMaximized)
        WindowState = WindowState.Maximized;
    }

    private void Window_StateChanged(object? sender, EventArgs e) => SaveWindowSettings();

    private void Window_Closed(object? sender, EventArgs e)
    {
      SaveWindowSettings();
      var (humanScore, computerScore) = CountPieces();
      AddLogEntry($"Durée de la session : {DateTime.Now - _sessionStarted:c}.");
      AddLogEntry($"Fermeture de l'application. Score final : joueur {humanScore}, ordinateur {computerScore}.");
    }

    private static string CreateLogFilePath()
    {
      var timestamp = DateTime.Now;
      string logPath;
      do
      {
        var fileName = $"FredOthelloReversi-{timestamp:yyyy-MM-dd_HH-mm-ss}.log";
        logPath = IOPath.Combine(AppContext.BaseDirectory, fileName);
        timestamp = timestamp.AddSeconds(1);
      }
      while (File.Exists(logPath));

      return logPath;
    }

    private void AddLogEntry(string message)
    {
      try
      {
        File.AppendAllText(_logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
      }
      catch (IOException exception)
      {
        ShowLogWriteError(exception.Message);
      }
      catch (UnauthorizedAccessException exception)
      {
        ShowLogWriteError(exception.Message);
      }
    }

    private void ShowLogWriteError(string message)
    {
      if (_logWriteErrorShown)
        return;

      _logWriteErrorShown = true;
      MessageBox.Show(
          $"Impossible d'enregistrer le fichier de log « {_logFilePath} » : {message}",
          "Othello | Reversi",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }

    private void SaveWindowSettings()
    {
      var bounds = WindowState == WindowState.Normal
        ? new Rect(Left, Top, Width, Height)
        : RestoreBounds;
      var settings = new WindowBounds(
        bounds.Left,
        bounds.Top,
        bounds.Width,
        bounds.Height,
        WindowState == WindowState.Maximized);
      var settingsPath = WindowSettingsPath;

      try
      {
        Directory.CreateDirectory(IOPath.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
      }
      catch (IOException exception)
      {
        MessageBox.Show(
          $"Impossible d'enregistrer les dimensions de la fenêtre : {exception.Message}",
          "Othello | Reversi",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }
      catch (UnauthorizedAccessException exception)
      {
        MessageBox.Show(
          $"Impossible d'enregistrer les dimensions de la fenêtre : {exception.Message}",
          "Othello | Reversi",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }
    }

    private void BuildBoard()
    {
      for (var index = 0; index < BoardSize; index++)
      {
        BoardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      }

      for (var row = 0; row < BoardSize; row++)
      {
        for (var column = 0; column < BoardSize; column++)
        {
          var button = new Button
          {
            Margin = new Thickness(1),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 102, 84)),
            Background = new SolidColorBrush(Color.FromRgb(23, 99, 78)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Tag = (row, column)
          };
          button.Click += Square_Click;
          Grid.SetRow(button, row);
          Grid.SetColumn(button, column);
          BoardGrid.Children.Add(button);
          _squares[row, column] = button;
        }
      }
    }

    private void StartNewGame()
    {
      AddLogEntry("Nouvelle partie.");
      _gameVersion++;
      _gameOver = false;
      _computerThinking = false;
      _currentPlayer = Human;
      _moveCount = 0;
      Array.Clear(_board);
      _board[3, 3] = Computer;
      _board[3, 4] = Human;
      _board[4, 3] = Human;
      _board[4, 4] = Computer;
      StatusText.Text = "Choisissez une case illuminée.";
      RefreshBoard();
    }

    private void NewGame_Click(object sender, RoutedEventArgs e) => StartNewGame();

    private async void Square_Click(object sender, RoutedEventArgs e)
    {
      if (_gameOver || _computerThinking || sender is not Button { Tag: ValueTuple<int, int> position })
        return;

      var move = GetLegalMoves(_board, Human)
        .FirstOrDefault(candidate => candidate.Row == position.Item1 && candidate.Column == position.Item2);
      if (move is null)
        return;

      ApplyMove(_board, move, Human);
      _moveCount++;
      AddLogEntry($"Coup du joueur : ligne {move.Row + 1}, colonne {move.Column + 1}.");
      _currentPlayer = Computer;
      await ContinueGameAsync(_gameVersion);
    }

    private async Task ContinueGameAsync(int gameVersion)
    {
      while (!_gameOver && gameVersion == _gameVersion)
      {
        var legalMoves = GetLegalMoves(_board, _currentPlayer);
        if (legalMoves.Count == 0)
        {
          if (GetLegalMoves(_board, -_currentPlayer).Count == 0)
          {
            FinishGame();
            return;
          }

          StatusText.Text = _currentPlayer == Computer
            ? "L'ordinateur passe son tour : aucun coup possible."
            : "Vous passez votre tour : aucun coup possible.";
          AddLogEntry(_currentPlayer == Computer
            ? "L'ordinateur passe son tour."
            : "Le joueur passe son tour.");
          _currentPlayer = -_currentPlayer;
          RefreshBoard();
          if (_currentPlayer == Human)
            return;
          continue;
        }

        if (_currentPlayer == Human)
        {
          StatusText.Text = "À vous : choisissez une case illuminée.";
          RefreshBoard();
          return;
        }

        _computerThinking = true;
        StatusText.Text = "L'ordinateur réfléchit…";
        RefreshBoard();
        await Task.Delay(450);
        if (gameVersion != _gameVersion || _gameOver)
          return;

        var computerMove = ChooseComputerMove(legalMoves);
        ApplyMove(_board, computerMove, Computer);
        _moveCount++;
        AddLogEntry($"Coup de l'ordinateur : ligne {computerMove.Row + 1}, colonne {computerMove.Column + 1}.");
        _currentPlayer = Human;
        _computerThinking = false;
      }
    }

    private void FinishGame()
    {
      _gameOver = true;
      _computerThinking = false;
      var (humanScore, computerScore) = CountPieces();
      AddLogEntry($"Partie terminée. Score : joueur {humanScore}, ordinateur {computerScore}.");
      StatusText.Text = humanScore == computerScore
        ? $"Égalité ! Vous terminez à {humanScore} partout."
        : humanScore > computerScore
          ? $"Bravo, vous gagnez {humanScore} à {computerScore} !"
          : $"L'ordinateur gagne {computerScore} à {humanScore}.";
      TurnLabel.Text = "PARTIE TERMINÉE";
      RefreshBoard();
    }

    private void RefreshBoard()
    {
      var legalMoves = !_gameOver && !_computerThinking && _currentPlayer == Human
        ? GetLegalMoves(_board, Human)
        : [];
      var legalSquares = legalMoves.Select(move => (move.Row, move.Column)).ToHashSet();

      for (var row = 0; row < BoardSize; row++)
      {
        for (var column = 0; column < BoardSize; column++)
        {
          var button = _squares[row, column];
          var content = new Grid();
          if (_board[row, column] != 0)
          {
            content.Children.Add(CreateDisc(_board[row, column]));
          }
          else if (legalSquares.Contains((row, column)))
          {
            content.Children.Add(new Ellipse
            {
              Width = 12,
              Height = 12,
              Fill = new SolidColorBrush(Color.FromArgb(190, 232, 185, 110)),
              HorizontalAlignment = HorizontalAlignment.Center,
              VerticalAlignment = VerticalAlignment.Center,
              IsHitTestVisible = false
            });
          }

          button.Content = content;
          button.IsEnabled = !_gameOver && !_computerThinking && _currentPlayer == Human
            && _board[row, column] == 0 && legalSquares.Contains((row, column));
        }
      }

      var (humanScore, computerScore) = CountPieces();
      PlayerScoreText.Text = humanScore.ToString();
      ComputerScoreText.Text = computerScore.ToString();
      MovesText.Text = $"{_moveCount} {(_moveCount == 1 ? "coup joué" : "coups joués")}";
      if (!_gameOver)
      {
        TurnLabel.Text = _currentPlayer == Human ? "À VOUS DE JOUER" : "AU TOUR DE L'ORDINATEUR";
      }
    }

    private static Ellipse CreateDisc(int player)
    {
      var disc = new Ellipse
      {
        Width = 38,
        Height = 38,
        StrokeThickness = 1.5,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
      };
      if (player == Human)
      {
        disc.Fill = new LinearGradientBrush(Color.FromRgb(52, 59, 56), Color.FromRgb(10, 13, 12), 45);
        disc.Stroke = new SolidColorBrush(Color.FromRgb(83, 96, 91));
      }
      else
      {
        disc.Fill = new LinearGradientBrush(Color.FromRgb(255, 255, 249), Color.FromRgb(205, 211, 204), 45);
        disc.Stroke = new SolidColorBrush(Color.FromRgb(174, 183, 176));
      }
      return disc;
    }

    private (int Human, int Computer) CountPieces()
    {
      var human = 0;
      var computer = 0;
      foreach (var piece in _board)
      {
        if (piece == Human)
          human++;
        else if (piece == Computer)
          computer++;
      }
      return (human, computer);
    }

    private static List<Move> GetLegalMoves(int[,] board, int player)
    {
      var moves = new List<Move>();
      for (var row = 0; row < BoardSize; row++)
      {
        for (var column = 0; column < BoardSize; column++)
        {
          if (board[row, column] != 0)
            continue;
          var flips = GetFlips(board, row, column, player);
          if (flips.Count > 0)
            moves.Add(new Move(row, column, flips));
        }
      }
      return moves;
    }

    private static List<(int Row, int Column)> GetFlips(int[,] board, int row, int column, int player)
    {
      var flips = new List<(int Row, int Column)>();
      foreach (var (rowStep, columnStep) in Directions)
      {
        var line = new List<(int Row, int Column)>();
        var nextRow = row + rowStep;
        var nextColumn = column + columnStep;
        while (IsOnBoard(nextRow, nextColumn) && board[nextRow, nextColumn] == -player)
        {
          line.Add((nextRow, nextColumn));
          nextRow += rowStep;
          nextColumn += columnStep;
        }
        if (line.Count > 0 && IsOnBoard(nextRow, nextColumn) && board[nextRow, nextColumn] == player)
          flips.AddRange(line);
      }
      return flips;
    }

    private static bool IsOnBoard(int row, int column) =>
      row >= 0 && row < BoardSize && column >= 0 && column < BoardSize;

    private static void ApplyMove(int[,] board, Move move, int player)
    {
      board[move.Row, move.Column] = player;
      foreach (var (row, column) in move.Flips)
        board[row, column] = player;
    }

    private static Move ChooseComputerMove(List<Move> moves)
    {
      return moves
        .Select(move => new
        {
          Move = move,
          Score = PositionValues[move.Row, move.Column] * 5 + move.Flips.Count + Random.Shared.Next(3)
        })
        .MaxBy(candidate => candidate.Score)!.Move;
    }

    private sealed record Move(int Row, int Column, List<(int Row, int Column)> Flips);
    private sealed record WindowBounds(double Left, double Top, double Width, double Height, bool IsMaximized);
  }
}
