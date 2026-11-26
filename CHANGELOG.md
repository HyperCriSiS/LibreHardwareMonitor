# Changelog

## Unreleased

### Added

- A warning in the status bar when PawnIO is missing or outdated, because CPU, motherboard and memory sensors then stay empty. Prompted by upstream issues 2348 and 2363.
- Temperature names for the MSI MEG Z690 Unify, including both T sensors, PCIe and M.2. Data from upstream issue 1306.

### Fixed

- After the computer wakes from sleep, the app opens the hardware again, so fans no longer stay at 0 RPM and full duty. Reported in upstream issue 2636.
- The core VIDs of AMD Zen 4 and Zen 5 CPUs use the SVI3 encoding and no longer read far too low and inverse to the load. Reported in upstream issues 2084 and 1684.
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
| Show why an NVIDIA GPU limits its clocks (NVML throttle reasons, hidden by default as advanced values) | 2600 | i-rinat |
| Decode NCT6792D temperatures like the newer Nuvoton chips and add the ASRock B550M-ITX/ac | 2590 | umbertotechnopreneur |
| Add the ASUS ProArt Z490-CREATOR 10G (adapted: fan names written out) | 2604 | Slion |
| Fall back to the WDDM temperature when ADL reports no AMD GPU core temperature | 2593 | tauCross |
| Add or update the MSI MAG B650 Tomahawk WiFi, ASUS ROG Crosshair VI Hero, ROG Crosshair X870E Glacial, ROG Crosshair X670E Hero and Gene, ROG Strix B850-I | 2645 | irusanov |
| Add the Gigabyte Z890 Aorus Master | 2512 | ForgiveForGit |
| Read the Cooler Master XG750 Plus Platinum PSU over USB | 2643 | loredavid |
