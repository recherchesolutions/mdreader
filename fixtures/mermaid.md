# Mermaid

A valid diagram:

```mermaid
graph TD
    A[Start] --> B{Decision}
    B -->|yes| C[Do the thing]
    B -->|no| D[Skip it]
```

An invalid diagram (must show the code block with an inline error note, never a
blank space):

```mermaid
graph TD
    A[Unclosed --> ???
    this is not valid mermaid
```
