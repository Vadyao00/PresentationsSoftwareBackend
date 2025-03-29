using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PresentationsSoftware.Models;
using System.Collections.Concurrent;

namespace PresentationsSoftware;

public class PresentationHub : Hub
{
    private readonly ApplicationDbContext _context;
    private static readonly Dictionary<string, PresentationUser> _connectedUsers = new Dictionary<string, PresentationUser>();
    
    // Cache of element position updates to reduce database writes
    private static readonly ConcurrentDictionary<int, (int PositionX, int PositionY, DateTime LastUpdate)> _pendingPositionUpdates = 
        new ConcurrentDictionary<int, (int, int, DateTime)>();
    
    // Timer to flush pending updates to the database
    private static System.Timers.Timer _flushTimer;
    
    // Debounce time for flushing updates (milliseconds)
    private const int FLUSH_INTERVAL = 200;
    
    // Static service provider that will be initialized in the constructor
    private static IServiceProvider _serviceProvider;

    public PresentationHub(ApplicationDbContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        
        // Store the service provider for creating scopes in the timer callback
        _serviceProvider = serviceProvider;
        
        // Initialize the timer if it hasn't been created yet
        if (_flushTimer == null)
        {
            _flushTimer = new System.Timers.Timer(FLUSH_INTERVAL);
            _flushTimer.Elapsed += async (sender, e) => await FlushPendingUpdates();
            _flushTimer.AutoReset = true;
            _flushTimer.Start();
        }
    }
    
    private async Task FlushPendingUpdates()
    {
        try 
        {
            var now = DateTime.UtcNow;
            var keysToUpdate = new List<int>();
            
            // Find elements that haven't been updated recently (movement has stopped)
            foreach (var kvp in _pendingPositionUpdates)
            {
                if ((now - kvp.Value.LastUpdate).TotalMilliseconds >= FLUSH_INTERVAL)
                {
                    keysToUpdate.Add(kvp.Key);
                }
            }
            
            if (keysToUpdate.Count == 0)
                return;
            
            // Create a scope from the static service provider
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            foreach (var elementId in keysToUpdate)
            {
                if (_pendingPositionUpdates.TryRemove(elementId, out var positionInfo))
                {
                    var dbElement = await dbContext.SlideElements.FindAsync(elementId);
                    
                    if (dbElement != null)
                    {
                        // Only update position-related properties
                        dbElement.PositionX = positionInfo.PositionX;
                        dbElement.PositionY = positionInfo.PositionY;
                    }
                }
            }
            
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log the error but don't crash the timer
            Console.Error.WriteLine($"Error in FlushPendingUpdates: {ex.Message}");
        }
    }

    public async Task JoinPresentation(int presentationId, string nickname, bool isCreator = false)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"presentation_{presentationId}");

        UserRole role = isCreator ? UserRole.Creator : UserRole.Viewer;

        var user = new PresentationUser
        {
            Id = Guid.NewGuid().GetHashCode(),
            ConnectionId = Context.ConnectionId,
            Nickname = nickname,
            PresentationId = presentationId,
            Role = role
        };

        _connectedUsers[Context.ConnectionId] = user;

        await Clients.Group($"presentation_{presentationId}").SendAsync("UserJoined", user);
    
        var updatedUsers = _connectedUsers.Values
            .Where(u => u.PresentationId == presentationId)
            .ToList();
    
        await Clients.Caller.SendAsync("UserList", updatedUsers);
    }

    public async Task ChangeUserRole(string connectionId, UserRole newRole)
    {
        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if (callerUser.Role != UserRole.Creator)
        {
            await Clients.Caller.SendAsync("Error", "Only the Creator can change user roles");
            return;
        }

        if (_connectedUsers.TryGetValue(connectionId, out var targetUser))
        {
            targetUser.Role = newRole;
            
            await Clients.Group($"presentation_{targetUser.PresentationId}")
                .SendAsync("UserRoleChanged", connectionId, newRole);
        }
    }

    public async Task AddSlide(int presentationId, int order)
    {
        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if ((callerUser.Role != UserRole.Creator) || 
            callerUser.PresentationId != presentationId)
        {
            await Clients.Caller.SendAsync("Error", "Only the Creator can add slides");
            return;
        }

        var slide = new Slide
        {
            PresentationId = presentationId,
            Order = order
        };

        _context.Slides.Add(slide);
        await _context.SaveChangesAsync();

        await Clients.Group($"presentation_{presentationId}")
            .SendAsync("SlideAdded", slide);
    }

    public async Task RemoveSlide(int slideId)
    {
        var slide = await _context.Slides.FindAsync(slideId);
        if (slide == null)
        {
            await Clients.Caller.SendAsync("Error", "Slide not found");
            return;
        }

        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if (callerUser.Role != UserRole.Creator || callerUser.PresentationId != slide.PresentationId)
        {
            await Clients.Caller.SendAsync("Error", "Only the Creator can remove slides");
            return;
        }

        _context.Slides.Remove(slide);
        await _context.SaveChangesAsync();

        await Clients.Group($"presentation_{slide.PresentationId}")
            .SendAsync("SlideRemoved", slideId);
    }

    public async Task AddElement(SlideElement element)
    {
        var slide = await _context.Slides.FindAsync(element.SlideId);
        if (slide == null)
        {
            await Clients.Caller.SendAsync("Error", "Slide not found");
            return;
        }

        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if ((callerUser.Role != UserRole.Editor && callerUser.Role != UserRole.Creator) || 
            callerUser.PresentationId != slide.PresentationId)
        {
            await Clients.Caller.SendAsync("Error", "Only Editors and the Creator can add elements");
            return;
        }

        _context.SlideElements.Add(element);
        await _context.SaveChangesAsync();

        await Clients.Group($"presentation_{slide.PresentationId}")
            .SendAsync("ElementAdded", element);
    }

    // New method optimized for position updates only
    public async Task UpdateElementPosition(int elementId, int positionX, int positionY)
    {
        // First, check permissions without hitting the database
        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if (callerUser.Role != UserRole.Editor && callerUser.Role != UserRole.Creator)
        {
            await Clients.Caller.SendAsync("Error", "Only Editors and the Creator can update elements");
            return;
        }
        
        // First time we're seeing this element? Verify it belongs to this presentation
        if (!_pendingPositionUpdates.ContainsKey(elementId))
        {
            // Do a quick check to make sure the element exists and belongs to this presentation
            var element = await _context.SlideElements
                .Include(e => e.Slide)
                .FirstOrDefaultAsync(e => e.Id == elementId);
                
            if (element == null)
            {
                await Clients.Caller.SendAsync("Error", "Element not found");
                return;
            }
            
            if (element.Slide.PresentationId != callerUser.PresentationId)
            {
                await Clients.Caller.SendAsync("Error", "Unauthorized to update this element");
                return;
            }
            
            // Create a lightweight copy of the element for broadcasting
            var elementCopy = new SlideElement
            {
                Id = element.Id,
                SlideId = element.SlideId,
                Type = element.Type,
                Content = element.Content,
                PositionX = positionX,
                PositionY = positionY,
                Width = element.Width,
                Height = element.Height,
                Color = element.Color,
                Style = element.Style
            };
            
            // Add/update in the pending updates collection
            _pendingPositionUpdates[elementId] = (positionX, positionY, DateTime.UtcNow);
            
            // Broadcast the update immediately without waiting for DB
            await Clients.Group($"presentation_{callerUser.PresentationId}")
                .SendAsync("ElementUpdated", elementCopy);
        }
        else
        {
            // Update position in our pending updates collection
            _pendingPositionUpdates[elementId] = (positionX, positionY, DateTime.UtcNow);
            
            // We need to broadcast an update - create a lightweight element object
            var elementUpdate = new
            {
                Id = elementId,
                PositionX = positionX,
                PositionY = positionY
            };
            
            // Broadcast just the position update
            await Clients.Group($"presentation_{callerUser.PresentationId}")
                .SendAsync("ElementPositionUpdated", elementUpdate);
        }
    }

    public async Task UpdateElement(SlideElement element)
    {
        var existingElement = await _context.SlideElements
            .Include(e => e.Slide)
            .FirstOrDefaultAsync(e => e.Id == element.Id);
        
        if (existingElement == null)
        {
            await Clients.Caller.SendAsync("Error", "Element not found");
            return;
        }

        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if ((callerUser.Role != UserRole.Editor && callerUser.Role != UserRole.Creator) || 
            callerUser.PresentationId != existingElement.Slide.PresentationId)
        {
            await Clients.Caller.SendAsync("Error", "Only Editors and the Creator can update elements");
            return;
        }

        // For content and style updates (not just position), always save to DB
        existingElement.Content = element.Content;
        existingElement.Width = element.Width;
        existingElement.Height = element.Height;
        existingElement.Color = element.Color;
        existingElement.Style = element.Style;
        
        // For position updates, also update the position
        existingElement.PositionX = element.PositionX;
        existingElement.PositionY = element.PositionY;

        await _context.SaveChangesAsync();

        // Remove from pending updates if it was there
        _pendingPositionUpdates.TryRemove(element.Id, out _);

        await Clients.Group($"presentation_{callerUser.PresentationId}")
            .SendAsync("ElementUpdated", existingElement);
    }

    public async Task RemoveElement(int elementId)
    {
        var element = await _context.SlideElements
            .Include(e => e.Slide)
            .FirstOrDefaultAsync(e => e.Id == elementId);
            
        if (element == null)
        {
            await Clients.Caller.SendAsync("Error", "Element not found");
            return;
        }

        if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var callerUser))
        {
            await Clients.Caller.SendAsync("Error", "User not found");
            return;
        }
        
        if ((callerUser.Role != UserRole.Editor && callerUser.Role != UserRole.Creator) || 
            callerUser.PresentationId != element.Slide.PresentationId)
        {
            await Clients.Caller.SendAsync("Error", "Only Editors and the Creator can remove elements");
            return;
        }

        // Remove from pending updates if it was there
        _pendingPositionUpdates.TryRemove(elementId, out _);

        _context.SlideElements.Remove(element);
        await _context.SaveChangesAsync();

        await Clients.Group($"presentation_{element.Slide.PresentationId}")
            .SendAsync("ElementRemoved", elementId);
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        if (_connectedUsers.TryGetValue(Context.ConnectionId, out var user))
        {
            await Clients.Group($"presentation_{user.PresentationId}")
                .SendAsync("UserLeft", Context.ConnectionId);

            var presentationId = user.PresentationId;
            var wasCreator = user.Role == UserRole.Creator;
        
            _connectedUsers.Remove(Context.ConnectionId);

            if (wasCreator)
            {
                var remainingUsers = _connectedUsers.Values
                    .Where(u => u.PresentationId == presentationId)
                    .ToList();
                
                if (remainingUsers.Any())
                {
                    var claimedCreator = remainingUsers
                        .FirstOrDefault(u => u.Nickname.Contains("(Creator)") || u.Nickname.Contains("[Creator]"));
                    
                    var nextCreator = claimedCreator ?? remainingUsers.First();
                
                    nextCreator.Role = UserRole.Creator;
                
                    await Clients.Group($"presentation_{presentationId}")
                        .SendAsync("UserRoleChanged", nextCreator.ConnectionId, UserRole.Creator);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}