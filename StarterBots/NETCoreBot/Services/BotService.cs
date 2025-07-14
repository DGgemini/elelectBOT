using NETCoreBot.Models;
using NETCoreBot.Enums;

namespace NETCoreBot.Services
{
    public class BotService
    {
        private Guid _botId;

        public void SetBotId(Guid botId)
        {
            _botId = botId;
        }

        public Guid GetBotId()
        {
            return _botId;
        }

        private Queue<(int x, int y)> _lastPositions = new Queue<(int x, int y)>();

        private int _powerUpsUsed = 0;

        private (int x, int y)? _lastKnownPowerUpLocation = null;

        private (int x, int y) _lastPosition = (-1, -1);

        private int _stuckCounter = 0;

        private int _lastMoveTick = 0;

        private Queue<(int x, int y)> _positionHistory = new Queue<(int x, int y)>(); // Track more positions

        private int _oscillationCounter = 0;

        public BotCommand ProcessState(GameState gameState)
        {
            var bot = gameState.Animals.FirstOrDefault(a => a.Id == _botId);
            var command = new BotCommand { Action = BotAction.Right };

            if (bot == null)
                return command;

            Console.WriteLine($"Tick: {gameState.Tick}");
            Console.WriteLine($"Bot Position: ({bot.X}, {bot.Y})");

            // Calculate field dimensions early for portal calculations
            int maxX = gameState.Cells.Max(c => c.X);
            int maxY = gameState.Cells.Max(c => c.Y);

            // Check if bot is stuck (same position for multiple ticks)
            if (_lastPosition.x == bot.X && _lastPosition.y == bot.Y)
            {
                _stuckCounter++;
                Console.WriteLine($"Bot stuck counter: {_stuckCounter}");
            }
            else
            {
                _stuckCounter = 0;
                _lastPosition = (bot.X, bot.Y);
            }

            // Add current position to history
            _positionHistory.Enqueue((bot.X, bot.Y));
            if (_positionHistory.Count > 6) _positionHistory.Dequeue(); // Keep last 6 positions

            // Check for oscillation patterns (moving between 2-3 positions)
            if (_positionHistory.Count >= 4)
            {
                var positions = _positionHistory.ToArray();

                // Check for 2-position oscillation (A->B->A->B)
                bool twoPositionLoop = positions.Length >= 4 &&
                    positions[positions.Length - 1].Equals(positions[positions.Length - 3]) &&
                    positions[positions.Length - 2].Equals(positions[positions.Length - 4]);

                // Check for 3-position loop (A->B->C->A->B->C)
                bool threePositionLoop = positions.Length >= 6 &&
                    positions[positions.Length - 1].Equals(positions[positions.Length - 4]) &&
                    positions[positions.Length - 2].Equals(positions[positions.Length - 5]) &&
                    positions[positions.Length - 3].Equals(positions[positions.Length - 6]);

                if (twoPositionLoop || threePositionLoop)
                {
                    _oscillationCounter++;
                    Console.WriteLine($"Bot oscillation detected! Counter: {_oscillationCounter} (Pattern: {(twoPositionLoop ? "2-pos" : "3-pos")})");

                    if (_oscillationCounter >= 3) // Oscillating for 3+ cycles
                    {
                        Console.WriteLine("BOT STUCK IN LOOP! Forcing random movement");
                        var randomMove = GetRandomEscapeMove(gameState, bot, maxX, maxY);
                        if (randomMove != null)
                        {
                            _oscillationCounter = 0;
                            _positionHistory.Clear(); // Clear history after breaking loop
                            return randomMove;
                        }
                    }
                }
                else
                {
                    _oscillationCounter = 0; // Reset if no oscillation detected
                }
            }

            // If stuck for 3+ ticks, force movement away from zookeepers
            if (_stuckCounter >= 3)
            {
                Console.WriteLine("BOT STUCK! Forcing emergency movement away from zookeepers");
                var emergencyMove = GetEmergencyEscapeMove(gameState, bot, maxX, maxY);
                if (emergencyMove != null)
                {
                    _stuckCounter = 0; // Reset counter after forced move
                    _positionHistory.Clear(); // Clear history after emergency move
                    return emergencyMove;
                }
            }

            // Check if bot is near edge (potential portal use)
            bool nearLeftEdge = bot.X <= 1;
            bool nearRightEdge = bot.X >= maxX - 1;
            bool nearTopEdge = bot.Y <= 1;
            bool nearBottomEdge = bot.Y >= maxY - 1;

            if (nearLeftEdge || nearRightEdge || nearTopEdge || nearBottomEdge)
            {
                Console.WriteLine($"Bot near edge: Left={nearLeftEdge}, Right={nearRightEdge}, Top={nearTopEdge}, Bottom={nearBottomEdge}");
            }

            var allPowerUps = gameState.Cells
                .Where(c =>
                    c.Content == CellContent.PowerPellet ||
                    c.Content == CellContent.ChameleonCloak ||
                    c.Content == CellContent.Scavenger ||
                    c.Content == CellContent.BigMooseJuice)
                .ToList();

            var nearbyPowerUps = allPowerUps
                .Where(c => CalculatePortalDistance(c.X, c.Y, bot.X, bot.Y, maxX, maxY) <= 10)
                .OrderBy(c => CalculatePortalDistance(c.X, c.Y, bot.X, bot.Y, maxX, maxY))
                .ToList();

            var pellets = gameState.Cells
                .Where(c => c.Content == CellContent.Pellet)
                .OrderBy(c => CalculatePortalDistance(c.X, c.Y, bot.X, bot.Y, maxX, maxY))
                .ToList();

            Cell? target = null;
            System.Console.WriteLine($"Bot is holding power-up: {bot.HeldPowerUp}");

            if (bot.HeldPowerUp != null)
            {
                bool shouldUsePowerUp = false;
                string powerUpType = bot.HeldPowerUp?.ToString() ?? string.Empty;

                Console.WriteLine($"Power-up string: {powerUpType}");

                switch (powerUpType)
                {
                    case "PowerPellet":
                        Console.WriteLine($"Using PowerPellet at ({bot.X}, {bot.Y})");
                        shouldUsePowerUp = true;
                        break;

                    case "ChameleonCloak":
                        Console.WriteLine($"Using ChameleonCloak at ({bot.X}, {bot.Y})");
                        var nearbyZookeepers = gameState.Zookeepers.Any(z => Math.Abs(z.X - bot.X) + Math.Abs(z.Y - bot.Y) <= 5);
                        shouldUsePowerUp = nearbyZookeepers;
                        break;

                    case "Scavenger":
                        Console.WriteLine($"Using Scavenger at ({bot.X}, {bot.Y})");
                        shouldUsePowerUp = true;
                        break;

                    case "BigMooseJuice":
                        Console.WriteLine($"Using BigMooseJuice at ({bot.X}, {bot.Y})");
                        shouldUsePowerUp = true;
                        break;

                    default:
                        Console.WriteLine($"Unknown power-up: {powerUpType}");
                        break;
                }

                if (shouldUsePowerUp)
                {
                    _powerUpsUsed++;
                    Console.WriteLine($"Planned Action: UseItem (activating {bot.HeldPowerUp}) - Total used: {_powerUpsUsed}");
                    return new BotCommand { Action = BotAction.UseItem };
                }
            }

            // MUCH more aggressive power-up prioritization
            if (nearbyPowerUps.Any())
            {
                target = nearbyPowerUps.First();
                Console.WriteLine($"Targeting nearby power-up: {target.Content} at ({target.X}, {target.Y})");
            }
            else if (allPowerUps.Any())
            {
                var closestPowerUp = allPowerUps.OrderBy(c => CalculatePortalDistance(c.X, c.Y, bot.X, bot.Y, maxX, maxY)).First();
                var closestPellet = pellets.FirstOrDefault();

                if (closestPellet != null)
                {
                    var distPowerUp = CalculatePortalDistance(closestPowerUp.X, closestPowerUp.Y, bot.X, bot.Y, maxX, maxY);
                    var distPellet = CalculatePortalDistance(closestPellet.X, closestPellet.Y, bot.X, bot.Y, maxX, maxY);

                    // Prefer power-ups unless pellet is MUCH closer
                    target = (distPowerUp <= distPellet * 30.0) ? closestPowerUp : closestPellet;
                    Console.WriteLine($"Choosing between power-up (portal dist: {distPowerUp}) and pellet (portal dist: {distPellet}) - chose: {target.Content}");
                }
                else
                {
                    target = closestPowerUp;
                    Console.WriteLine($"No pellets available, targeting power-up: {target.Content}");
                }
            }
            else
            {
                target = pellets.FirstOrDefault();
                Console.WriteLine("No power-ups available, targeting pellets");
            }

            if (target == null)
                return command;

            // Add this after target selection
            if (target == null && _lastKnownPowerUpLocation.HasValue)
            {
                // Head toward last known power-up location
                var lastLoc = _lastKnownPowerUpLocation.Value;
                var fakeTarget = new Cell { X = lastLoc.x, Y = lastLoc.y, Content = CellContent.PowerPellet };
                target = fakeTarget;
                Console.WriteLine($"No targets found, heading to last known power-up location: ({lastLoc.x}, {lastLoc.y})");
            }

            var dangerZones = new HashSet<(int X, int Y)>();
            // Larger danger radius for better avoidance
            int dangerRadius = (target != null && target.Content != CellContent.Pellet) ? 8 : 12;

            // Add immediate threat detection
            var immediateThreats = gameState.Zookeepers.Where(z =>
                CalculatePortalDistance(z.X, z.Y, bot.X, bot.Y, maxX, maxY) <= 4).ToList();

            if (immediateThreats.Any())
            {
                Console.WriteLine($"IMMEDIATE THREAT: {immediateThreats.Count} zookeepers within 4 units!");
                dangerRadius = Math.Max(dangerRadius, 15); // Increase danger radius when threats are close
            }

            foreach (var zk in gameState.Zookeepers)
            {
                // Current zookeeper position danger zone
                for (int dx = -dangerRadius; dx <= dangerRadius; dx++)
                {
                    for (int dy = -dangerRadius; dy <= dangerRadius; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > dangerRadius)
                            continue;

                        int zx = (zk.X + dx + maxX + 1) % (maxX + 1);
                        int zy = (zk.Y + dy + maxY + 1) % (maxY + 1);
                        dangerZones.Add((zx, zy));
                    }
                }

                // Predict zookeeper movement (multiple steps ahead)
                for (int step = 1; step <= 2; step++)
                {
                    foreach (var (adx, ady) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                    {
                        int predX = (zk.X + adx * step + maxX + 1) % (maxX + 1);
                        int predY = (zk.Y + ady * step + maxY + 1) % (maxY + 1);

                        var predCell = gameState.Cells.FirstOrDefault(c => c.X == predX && c.Y == predY);
                        if (predCell != null && predCell.Content != CellContent.Wall)
                        {
                            // Add smaller danger zone around predicted positions
                            int predRadius = Math.Max(2, dangerRadius - step * 2);
                            for (int pdx = -predRadius; pdx <= predRadius; pdx++)
                            {
                                for (int pdy = -predRadius; pdy <= predRadius; pdy++)
                                {
                                    if (Math.Abs(pdx) + Math.Abs(pdy) <= predRadius)
                                    {
                                        int pzx = (predX + pdx + maxX + 1) % (maxX + 1);
                                        int pzy = (predY + pdy + maxY + 1) % (maxY + 1);
                                        dangerZones.Add((pzx, pzy));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"Total danger zones: {dangerZones.Count}, Danger radius: {dangerRadius}");

            // Use BFS pathfinding to get the path to the target
            var path = FindPath(gameState, (bot.X, bot.Y), (target.X, target.Y), dangerZones);

            if (path.Count >= 2)
            {
                var next = path[1];

                // Handle portal wraparound in movement calculation
                int dx = next.x - bot.X;
                int dy = next.y - bot.Y;

                // Adjust for portal wraparound
                if (dx > (maxX + 1) / 2) dx -= (maxX + 1);  // Going left through portal
                if (dx < -(maxX + 1) / 2) dx += (maxX + 1); // Going right through portal
                if (dy > (maxY + 1) / 2) dy -= (maxY + 1);  // Going up through portal
                if (dy < -(maxY + 1) / 2) dy += (maxY + 1); // Going down through portal

                Console.WriteLine($"Movement: from ({bot.X},{bot.Y}) to ({next.x},{next.y}) -> dx={dx}, dy={dy}");

                if (dx == 1 && dy == 0) command.Action = BotAction.Right;
                else if (dx == -1 && dy == 0) command.Action = BotAction.Left;
                else if (dx == 0 && dy == 1) command.Action = BotAction.Down;
                else if (dx == 0 && dy == -1) command.Action = BotAction.Up;
                else
                {
                    Console.WriteLine($"WARNING: Invalid movement delta dx={dx}, dy={dy}");
                    // Fallback to simple direction
                    if (Math.Abs(dx) > Math.Abs(dy))
                        command.Action = dx > 0 ? BotAction.Right : BotAction.Left;
                    else
                        command.Action = dy > 0 ? BotAction.Down : BotAction.Up;
                }

                Console.WriteLine($"Planned Action: {command.Action} (pathfinding toward {(target.Content == CellContent.Pellet ? "pellet" : "power-up")})");
                return command;
            }

            // fallback: original logic if no path found
            var directions = new List<(BotAction action, int dx, int dy)>
        {
            (BotAction.Up, 0, -1),
            (BotAction.Down, 0, 1),
            (BotAction.Left, -1, 0),
            (BotAction.Right, 1, 0)
        };

            BotCommand? fallbackCommand = null;

            foreach (var (action, dx, dy) in directions)
            {
                int newX = (bot.X + dx + maxX + 1) % (maxX + 1);
                int newY = (bot.Y + dy + maxY + 1) % (maxY + 1);

                var cell = gameState.Cells.FirstOrDefault(c => c.X == newX && c.Y == newY);

                if (dangerZones.Contains((newX, newY)))
                {
                    Console.WriteLine($"Skipped {action} (danger zone)");
                    continue;
                }

                // Check minimum distance to zookeepers
                var minZookeeperDist = gameState.Zookeepers.Any() ?
                    gameState.Zookeepers.Min(z => CalculatePortalDistance(z.X, z.Y, newX, newY, maxX, maxY)) : 999;

                if (minZookeeperDist < 5 && _stuckCounter < 3) // Be less strict when stuck
                {
                    Console.WriteLine($"Skipped {action} (too close to zookeeper: {minZookeeperDist})");
                    continue;
                }

                if (cell != null && cell.Content != CellContent.Wall)
                {
                    int currentDistance = CalculatePortalDistance(bot.X, bot.Y, target.X, target.Y, maxX, maxY);
                    int newDistance = CalculatePortalDistance(newX, newY, target.X, target.Y, maxX, maxY);

                    if (newDistance < currentDistance)
                    {
                        command.Action = action;
                        Console.WriteLine($"Planned Action: {command.Action} (toward target, zookeeper dist: {minZookeeperDist})");
                        return command;
                    }

                    fallbackCommand ??= new BotCommand { Action = action };
                }
            }

            if (fallbackCommand != null)
            {
                command = fallbackCommand;
                Console.WriteLine($"Planned Action: {command.Action} (fallback)");
            }

            Console.WriteLine($"Planned Action: {command.Action}");
            _lastPositions.Enqueue((bot.X, bot.Y));
            if (_lastPositions.Count > 5) _lastPositions.Dequeue();

            // Update last known power-up location
            if (allPowerUps.Any())
            {
                var closestPowerUp = allPowerUps.OrderBy(c => Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y)).First();
                _lastKnownPowerUpLocation = (closestPowerUp.X, closestPowerUp.Y);
            }

            Console.WriteLine($"Position history: {string.Join(" -> ", _positionHistory.Select(p => $"({p.x},{p.y})"))}");
            Console.WriteLine($"Stuck counter: {_stuckCounter}, Oscillation counter: {_oscillationCounter}");

            return command;
        }

        // Add this helper method inside BotService:
        private List<(int x, int y)> FindPath(GameState gameState, (int x, int y) start, (int x, int y) goal, HashSet<(int, int)> dangerZones)
        {
            var queue = new Queue<List<(int, int)>>();
            var visited = new HashSet<(int, int)>();
            queue.Enqueue(new List<(int, int)> { start });
            visited.Add(start);

            int maxX = gameState.Cells.Max(c => c.X);
            int maxY = gameState.Cells.Max(c => c.Y);

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                var (cx, cy) = path.Last();

                if ((cx, cy) == goal)
                    return path;

                foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                {
                    int nx = (cx + dx + maxX + 1) % (maxX + 1);
                    int ny = (cy + dy + maxY + 1) % (maxY + 1);

                    if (visited.Contains((nx, ny))) continue;

                    var cell = gameState.Cells.FirstOrDefault(c => c.X == nx && c.Y == ny);
                    if (cell == null || cell.Content == CellContent.Wall) continue;

                    // Strict danger zone checking
                    if (dangerZones.Contains((nx, ny))) continue;

                    // Avoid recent positions (but be more lenient when stuck or oscillating)
                    bool isStuckOrOscillating = _stuckCounter >= 2 || _oscillationCounter >= 2;
                    if (!isStuckOrOscillating && _lastPositions.Contains((nx, ny))) continue;

                    // Additional safety: don't get too close to any zookeeper (unless really stuck)
                    var minZookeeperDist = gameState.Zookeepers.Any() ?
                        gameState.Zookeepers.Min(z => CalculatePortalDistance(z.X, z.Y, nx, ny, maxX, maxY)) : 999;

                    int safeDistance = isStuckOrOscillating ? 3 : 6; // Reduce safe distance when stuck
                    if (minZookeeperDist < safeDistance) continue;

                    var newPath = new List<(int, int)>(path) { (nx, ny) };
                    queue.Enqueue(newPath);
                    visited.Add((nx, ny));
                }
            }
            return new List<(int, int)>();
        }

        private int CalculatePortalDistance(int x1, int y1, int x2, int y2, int maxX, int maxY)
        {
            // Calculate normal distance
            int normalDistX = Math.Abs(x2 - x1);
            int normalDistY = Math.Abs(y2 - y1);

            // Calculate wraparound distance
            int wrapDistX = Math.Min(normalDistX, (maxX + 1) - normalDistX);
            int wrapDistY = Math.Min(normalDistY, (maxY + 1) - normalDistY);

            return wrapDistX + wrapDistY;
        }

        private BotCommand? GetEmergencyEscapeMove(GameState gameState, Animal bot, int maxX, int maxY)
        {
            var emergencyDirections = new List<(BotAction action, int dx, int dy)>
        {
            (BotAction.Up, 0, -1),
            (BotAction.Down, 0, 1),
            (BotAction.Left, -1, 0),
            (BotAction.Right, 1, 0)
        };

            // Find the move that maximizes distance from all zookeepers
            var bestMove = emergencyDirections
                .Select(dir => new
                {
                    Action = dir.action,
                    NewX = (bot.X + dir.dx + maxX + 1) % (maxX + 1),
                    NewY = (bot.Y + dir.dy + maxY + 1) % (maxY + 1)
                })
                .Where(move =>
                {
                    var cell = gameState.Cells.FirstOrDefault(c => c.X == move.NewX && c.Y == move.NewY);
                    return cell != null && cell.Content != CellContent.Wall;
                })
                .Select(move => new
                {
                    move.Action,
                    move.NewX,
                    move.NewY,
                    MinZookeeperDistance = gameState.Zookeepers.Any() ?
                        gameState.Zookeepers.Min(z => CalculatePortalDistance(z.X, z.Y, move.NewX, move.NewY, maxX, maxY)) : 999,
                    AvgZookeeperDistance = gameState.Zookeepers.Any() ?
                        gameState.Zookeepers.Average(z => CalculatePortalDistance(z.X, z.Y, move.NewX, move.NewY, maxX, maxY)) : 999,
                    IsRecentPosition = _positionHistory.Contains((move.NewX, move.NewY))
                })
                .OrderBy(move => move.IsRecentPosition) // Prefer moves to new positions
                .ThenByDescending(move => move.MinZookeeperDistance)
                .ThenByDescending(move => move.AvgZookeeperDistance)
                .FirstOrDefault();

            if (bestMove != null)
            {
                Console.WriteLine($"EMERGENCY ESCAPE: Moving {bestMove.Action} - Min zookeeper dist: {bestMove.MinZookeeperDistance}");
                return new BotCommand { Action = bestMove.Action };
            }

            return null;
        }

        private BotCommand? GetRandomEscapeMove(GameState gameState, Animal bot, int maxX, int maxY)
        {
            var allDirections = new List<(BotAction action, int dx, int dy)>
        {
            (BotAction.Up, 0, -1),
            (BotAction.Down, 0, 1),
            (BotAction.Left, -1, 0),
            (BotAction.Right, 1, 0)
        };

            // Get all valid moves (not into walls)
            var validMoves = allDirections
                .Select(dir => new
                {
                    Action = dir.action,
                    NewX = (bot.X + dir.dx + maxX + 1) % (maxX + 1),
                    NewY = (bot.Y + dir.dy + maxY + 1) % (maxY + 1)
                })
                .Where(move =>
                {
                    var cell = gameState.Cells.FirstOrDefault(c => c.X == move.NewX && c.Y == move.NewY);
                    return cell != null && cell.Content != CellContent.Wall;
                })
                .ToList();

            if (!validMoves.Any()) return null;

            // Try to avoid recent positions first
            var historyPositions = _positionHistory.ToHashSet();
            var newMoves = validMoves.Where(m => !historyPositions.Contains((m.NewX, m.NewY))).ToList();

            if (newMoves.Any())
            {
                // Pick a random move that avoids recent positions
                var random = new Random();
                var chosenMove = newMoves[random.Next(newMoves.Count)];
                Console.WriteLine($"RANDOM ESCAPE: Moving {chosenMove.Action} to avoid loop");
                return new BotCommand { Action = chosenMove.Action };
            }
            else
            {
                // If all moves lead to recent positions, just pick the one furthest from zookeepers
                var safestMove = validMoves
                    .Select(move => new
                    {
                        move.Action,
                        move.NewX,
                        move.NewY,
                        MinZookeeperDistance = gameState.Zookeepers.Any() ?
                            gameState.Zookeepers.Min(z => CalculatePortalDistance(z.X, z.Y, move.NewX, move.NewY, maxX, maxY)) : 999
                    })
                    .OrderByDescending(move => move.MinZookeeperDistance)
                    .First();

                Console.WriteLine($"FORCED ESCAPE: Moving {safestMove.Action} (safest available)");
                return new BotCommand { Action = safestMove.Action };
            }
        }
    }
}
