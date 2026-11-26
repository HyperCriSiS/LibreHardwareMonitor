# Changelog

## Unreleased

### Added

- Temperature names for the MSI MEG Z690 Unify, including both T sensors, PCIe and M.2. Data from upstream issue 1306.
### Fixed

- Nuvoton NCT6683D, NCT6686D and NCT6687D fans show no value instead of 65535 RPM while the chip has no reading. Reported in upstream issue 2640.
- NVIDIA GPU fans show no value instead of impossible speeds above 20000 RPM. Reported in upstream issue 1006.
- The voltage sensors of the 12VHPWR pins on ASUS Astral graphics cards no longer share an identifier with the GPU core voltage. Reported in upstream issue 2615.
- The sensor mapping no longer fails when two temperatures share an identifier.

### Adopted from upstream

Changes taken from open pull requests of the original LibreHardwareMonitor project. Credit goes to their authors.

| Change | Upstream PR | Author |
|---|---|---|
| Support the WireView Pro II Noctua Edition | 2644 | geretti |
| Add the MOS fan on the ASRock X870E Taichi | 2641 | Betalos |
| Ignore zero core temperature readings on NVIDIA Blackwell GPUs | 2624 | st7105 |
| Decode NCT6792D temperatures like the newer Nuvoton chips and add the ASRock B550M-ITX/ac | 2590 | umbertotechnopreneur |
| Add the ASUS ProArt Z490-CREATOR 10G (adapted: fan names written out) | 2604 | Slion |
| Fall back to the WDDM temperature when ADL reports no AMD GPU core temperature | 2593 | tauCross |
| Add or update the MSI MAG B650 Tomahawk WiFi, ASUS ROG Crosshair VI Hero, ROG Crosshair X870E Glacial, ROG Crosshair X670E Hero and Gene, ROG Strix B850-I | 2645 | irusanov |
| Add the Gigabyte Z890 Aorus Master | 2512 | ForgiveForGit |
| Read the Cooler Master XG750 Plus Platinum PSU over USB | 2643 | loredavid |
