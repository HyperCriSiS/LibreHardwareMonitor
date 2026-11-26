# Changelog

## Unreleased

### Fixed

- Nuvoton NCT6683D, NCT6686D and NCT6687D fans show no value instead of 65535 RPM while the chip has no reading. Reported in upstream issue 2640.
- NVIDIA GPU fans show no value instead of impossible speeds above 20000 RPM. Reported in upstream issue 1006.

### Adopted from upstream

Changes taken from open pull requests of the original LibreHardwareMonitor project. Credit goes to their authors.

| Change | Upstream PR | Author |
|---|---|---|
| Support the WireView Pro II Noctua Edition | 2644 | geretti |
| Add the MOS fan on the ASRock X870E Taichi | 2641 | Betalos |
| Ignore zero core temperature readings on NVIDIA Blackwell GPUs | 2624 | st7105 |
