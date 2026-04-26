using UnityEngine;

public class ServerInputContextManager : InputContextManager
{
    public void Start()
    {
        CurrentContext = new MainInputContext();
        CurrentContext.Activate();
    }
}
