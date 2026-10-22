# NVIDIA files (not included)

The installer bundles two NVIDIA files, which this repository does not include:

- `nvngx_dlss.dll`: NVIDIA's signed DLSS runtime.
- `nvngx_dlssnr.dll`: the DLSS 5 neural-rendering model. On RTX 20, 30 and 40 cards the neural pass needs the community-modified build, which is distributed through the RenoDX Discord and is not signed by NVIDIA.

Put both here before running `build.sh` to build the installer. The app itself also works without bundled files: pick them under **NVIDIA files** in the app.
