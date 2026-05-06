using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading;
using MySql.Data.MySqlClient;

[Route("[controller]")]
public class PushController : Controller
{
    private static readonly Dictionary<long, string> MessageBroker = new();
    private static long GlobalIDCounter = 5000;
    private static readonly object LockObject = new object();

    [HttpGet]
    [Route("GetMsg")]
    public IActionResult GetMsg()
    {
        long myId;
        lock (LockObject)
        {
            myId = GlobalIDCounter++;
            MessageBroker[myId] = "N/A";
        }

        try
        {
            lock (LockObject)
            {
                while (MessageBroker[myId] == "N/A")
                {
                    if (!Monitor.Wait(LockObject, TimeSpan.FromSeconds(30))) // 30-second timeout
                    {
                        MessageBroker.Remove(myId);
                        return StatusCode(408, "Request timeout");
                    }
                }
                string result = MessageBroker[myId];
                MessageBroker.Remove(myId);
                return Ok(result);
            }
        }
        catch
        {
            lock (LockObject)
            {
                MessageBroker.Remove(myId);
            }
            throw;
        }
    }

    [HttpGet]
    [Route("SendMsg/{UserName}/{UserMsg}")]
    public IActionResult SendMsg(string UserName, string UserMsg)
    {
        string msg = $"{UserName}:{UserMsg}";
        lock (LockObject)
        {
            var keys = new List<long>(MessageBroker.Keys);
            foreach (var id in keys)
            {
                MessageBroker[id] = msg;
            }
            Monitor.PulseAll(LockObject);
        }
        return Ok("Message sent to all waiting clients");
    }
}