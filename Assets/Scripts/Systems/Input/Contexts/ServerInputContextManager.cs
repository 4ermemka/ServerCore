using UnityEngine;

public class ServerInputContextManager : InputContextManager
{
    public void Start()
    {
        SetRootContext(new MainInputContext());
    }
}
