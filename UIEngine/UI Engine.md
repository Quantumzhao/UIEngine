# UI Engine

```sequence
Runtime Objects -> Object Tree: Retrieve VISIBLE object information
Object Tree -> Runtime Objects: Modify object data
Object Tree -> Visual Tree: Inform changes on the object nodes (output)
Visual Tree -> Object Tree: Apply user actions on the object nodes (input)
Visual Tree -> UI: Notify controls to respond to actions
UI -> Visual Tree: User actions
Note right of Object Tree: The abstraction of runtime object model
Note right of Visual Tree: The abstraction of UI
```

## APIs

### UI Side

#### Navigate To

#### Show

#### Select

#### Fill

### Memory/Runtime Side

#### Visible

