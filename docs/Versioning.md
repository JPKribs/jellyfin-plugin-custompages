# Versioning

Releases use a four part version, `JJ.JJ.F.B`, that matches the supported Jellyfin version with the plugin's own feature and bug count:

```
12.0.1.0
└──┘ └┬┘
 │    └── 1 = Plugin feature release
 │        0 = Plugin bug/patch release within that feature
 │
 └─── 12.0 = Jellyfin version this build was tested/released for
```

Targets **Jellyfin 12.2.x** (`net10.0`, ABI `12.2.0.0`).
