using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace SignalRChat.Hubs
{
    public class ChatHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"Connected: {Context.ConnectionId}");
            return base.OnConnectedAsync();
        }

        public async Task SendMessage(string userName, string userMsg)
        {
            var senderId = Context.ConnectionId;
            Console.WriteLine($"Message received from {userName}: {userMsg} (ConnectionId: {senderId})");

            // Küldjük az üzenetet és a sender connectionId-t
            await Clients.All.SendAsync("ReceiveMessage", userName, userMsg, senderId);
        }

        // Ezzel a kliens lekérheti a saját connectionId-ját
        public string GetConnectionId()
        {
            return Context.ConnectionId;
        }
    }
}
