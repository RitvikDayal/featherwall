# Third-party notices

## NuGet dependencies

| Package | License | Purpose |
|---|---|---|
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (Direct3D11, DXGI, DirectComposition, Direct2D1) | MIT | DirectX bindings |

That is the entire third-party dependency surface. GDI+ image decoding and text rendering come
from the Windows Desktop framework itself, not a package.

## Built-in gallery media

Every gallery entry is public domain or CC0; the manifest ([src/FeatherWall/Gallery/gallery.json](src/FeatherWall/Gallery/gallery.json)) records per entry: the source page, author, license, and (for Wikimedia Commons files) the SHA-1 checksum the download is verified against.

- **NASA imagery/video** — public domain in the United States. Used per [NASA's media usage guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/): "Courtesy NASA" attribution, no endorsement implied, NASA insignia excluded.
- **Wikimedia Commons files** — individually verified CC0 / public-domain dedications; see each entry's `sourcePage`.

FeatherWall downloads gallery media directly from the original host at the user's request; the application does not redistribute or rehost any of it.

## Documentation screenshots

The screenshots in [docs/media/](docs/media) are real captures of FeatherWall running, so they
contain gallery media. Those images **are** redistributed as part of this repository. Every one
is CC0 or public domain and permits it:

| Screenshot | Media shown | Author | License | Source |
|---|---|---|---|---|
| `hero.jpg` | Marmolada, Italy | Marco Bonomo | CC0 | [Commons](https://commons.wikimedia.org/wiki/File:Marmolada,_Italy.jpg) |
| `live-video.jpg` | Time-lapse of Aurora Borealis in Norway | Christer Olsen | CC0 | [Commons](https://commons.wikimedia.org/wiki/File:Time-lapse_of_Aurora_Borealis_in_Norway.webm) |
| `clock-bahnschrift.jpg` | Hubble Ultra Deep Field 2014 | NASA/ESA, H. Teplitz & M. Rafelski (IPAC/Caltech), A. Koekemoer (STScI) et al. | Public domain | [Commons](https://commons.wikimedia.org/wiki/File:NASA-HS201427a-HubbleUltraDeepField2014-20140603.jpg) |
| `clock-cascadia.jpg` | The Earth seen from Apollo 17 ("Blue Marble") | NASA / Apollo 17 crew | Public domain | [Commons](https://commons.wikimedia.org/wiki/File:The_Earth_seen_from_Apollo_17.jpg) |
| `clock-georgia.jpg` | Alone in the unspoilt wilderness | David Marcu | CC0 | [Commons](https://commons.wikimedia.org/wiki/File:Alone_in_the_unspoilt_wilderness_(Unsplash).jpg) |
| `tray-menu.png` | (aurora time-lapse, as above) | Christer Olsen | CC0 | as above |
| `settings-*.png` | none — application UI only | — | — | — |

NASA imagery carries "Courtesy NASA" attribution and implies no endorsement.
