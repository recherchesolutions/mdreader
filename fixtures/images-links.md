# Images and links

A relative image (resolves against the document directory):

![Local diagram](images/diagram.png)

An image climbing one parent level (allowed, default limit is 3):

![Sibling folder](../shared/logo.png)

An image escaping too far (must be refused):

![Escape attempt](../../../../../../etc/passwd.png)

A remote image (blocked by default, placeholder shown):

![Tracking pixel](https://example.com/pixel.png)

Links: [relative](docs/readme.md), [absolute](https://example.com),
[mail](mailto:test@example.com), [anchor](#images-and-links).
