using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NPR_ValheimModUtils
{
    public static class Extensions
    {
        /// <summary>
        /// try/catch the delegate chain so that it doesnt break on the first failing Delegate.
        /// </summary>
        /// <param name="events"></param>
        public static void SafeInvoke(this Action events)
        {
            if (events == null)
            {
                return;
            }

            Delegate[] invocationList = events.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                Action action = (Action)invocationList[i];
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ModUtilsManager.Log.LogWarning($"Exception thrown at event {new StackFrame(1).GetMethod().Name} in {action.Method.DeclaringType.Name}.{action.Method.Name}:\n{ex}");
                }
            }
        }
    }
}
