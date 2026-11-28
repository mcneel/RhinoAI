# UI

Eto.Forms is a cross-platform UI framework. All UI in Rhino is done in Eto.

## Architecture

- Use MVVM Patterns
- Using Eto's Binding system to bind the View Model to he UI
- Every UI Parent will have a ViewModel named ParentViewModel
- Other UI elements may also need a ViewModel such as a CustomCell

## Misc

- Use padding and spacing consistent with the rest of Rhino where possible
- Use Colours consistent with Rhino where possible
- Do not use Toast notifications

If lost refer to -> https://developer.rhino3d.com/guides/eto/
