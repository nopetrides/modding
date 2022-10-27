using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SomethingNearby
{
    /// <summary>
    /// Script that manages the message queue
    /// </summary>
    internal class MyMessagesManager
    {
        private Queue<string> queuedMessages = new Queue<string>();

        public void QueueMessage(string message)
        {
            if (SomethingNearby.Instance.ShowOnScreenMessages)
            {
                queuedMessages.Enqueue(message);
                if (!SomethingNearby.Instance.IsScreenMessageShowing)
                {
                    SomethingNearby.Instance.ShowNextQueuedMessage();
                }
            }
        }

        public string GetNextQueuedMessage()
        {
            if (queuedMessages.Count > 0)
            {   
                return queuedMessages.Dequeue();
            }
            else
            {
                return null;
            }
        }


    }
}
