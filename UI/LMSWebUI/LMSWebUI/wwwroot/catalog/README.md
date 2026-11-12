# Laundry item catalogue

This directory defines the centrally managed laundry image contract.

- `laundry-item-catalogue.json` lists the repository baseline item keys and deterministic relative paths.
- Target assets are individual 400x400 WebP files under `laundry-items/<service>/<category>/<item>.webp`.
- Every manifest entry is currently marked `placeholder` because no approved garment source images are present.
- Do not replace entries with unrelated images or generic basket icons.
- Before deployment, obtain approved garment assets, verify each image represents its exact item, optimize to 400x400 WebP, update `assetStatus` to `approved`, and populate the corresponding `ImagePath` on the item-master records through the approved deployment process.
- Tenant/store-specific active items must be exported from the authenticated API/database and reconciled with this manifest before final approval.
