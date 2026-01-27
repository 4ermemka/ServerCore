using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DragMove : InputHandler
{
    [SerializeField]
    Color ActiveColor = Color.green;
    [SerializeField]
    Color DeactiveColor = Color.white;

    protected override void SubscribeEvents()
    {
        SubscribeToAction(
            actionName: "ToggleCompass",
            onPressed: Active,
            onHeld: null,
            onReleased: Deactive
        );
    }
    
    protected override void UnsubscribeEvents()
    {
    
    }

    protected void Start()
    {
        Deactive();
    }

    private void Active()
    {
        var image = gameObject.GetComponent<SpriteRenderer>();
        image.color = ActiveColor;
    }

    private void Deactive()
    {
        var image = gameObject.GetComponent<SpriteRenderer>();
        image.color = DeactiveColor;
    }
}
