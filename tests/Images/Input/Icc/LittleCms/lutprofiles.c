/* Copyright (c) PinataLabs.
   Licensed under the Apache License, Version 2.0. */
/* Writes synthetic LUT-based test profiles with Little CMS 2 (MIT).
   Each LUT approximates an Adobe RGB (1998)-like space (or a gamma 2.2 gray) that Little CMS builds,
   through curves, matrices and CLUT grids chosen so that every element of each tag type does work. */
#include "lcms2.h"
#include <math.h>
#include <stdio.h>

static double Clip(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
static cmsUInt16Number Word(double v) { return (cmsUInt16Number) floor(Clip(v) * 65535.0 + 0.5); }
static cmsToneCurve* Gamma(double g) { return cmsBuildGamma(NULL, g); }
static cmsToneCurve* Table(int n, double g) {
  cmsUInt16Number t[4096];
  for (int i = 0; i < n; i++) t[i] = Word(pow(i / (double) (n - 1), g));
  return cmsBuildTabulatedToneCurve16(NULL, n, t); }
static cmsToneCurve* SrgbCurve(void) {
  double p[5] = { 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045 };
  return cmsBuildParametricToneCurve(NULL, 4, p); }
static cmsToneCurve* SrgbInverse(void) { cmsToneCurve* c = SrgbCurve(); cmsToneCurve* r = cmsReverseToneCurve(c); cmsFreeToneCurve(c); return r; }
static double Eval(cmsToneCurve* c, double v) { return cmsEvalToneCurveFloat(c, (cmsFloat32Number) Clip(v)); }
static double EvalInv(cmsToneCurve* c, double v) {
  cmsToneCurve* r = cmsReverseToneCurve(c); double y = Eval(r, v); cmsFreeToneCurve(r); return y; }
static void Mul3(const double m[9], const double v[3], double r[3]) {
  for (int i = 0; i < 3; i++) r[i] = m[3 * i] * v[0] + m[3 * i + 1] * v[1] + m[3 * i + 2] * v[2]; }
static void Invert3(const double m[9], double r[9]) {
  double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
  double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
  r[0] = (e * i - f * h) / det; r[1] = (c * h - b * i) / det; r[2] = (b * f - c * e) / det;
  r[3] = (f * g - d * i) / det; r[4] = (a * i - c * g) / det; r[5] = (c * d - a * f) / det;
  r[6] = (d * h - e * g) / det; r[7] = (b * g - a * h) / det; r[8] = (a * e - b * d) / det; }

static cmsHTRANSFORM rgbToXyz, xyzToRgb, rgbToLab, labToRgb, grayToLab, labToGray;
static const double Enc = 32768.0 / 65535.0;   /* PCS XYZ to the 0..1 encoding inside the tags */

/* Device RGB/gray to the 0..1 PCS encoding, and back. */
static void RgbToXyzN(const double rgb[3], double n[3]) {
  cmsCIEXYZ v; cmsDoTransform(rgbToXyz, rgb, &v, 1); n[0] = v.X * Enc; n[1] = v.Y * Enc; n[2] = v.Z * Enc; }
static void XyzNToRgb(const double n[3], double rgb[3]) {
  cmsCIEXYZ v = { n[0] / Enc, n[1] / Enc, n[2] / Enc }; cmsDoTransform(xyzToRgb, &v, rgb, 1); }
static void LabToN(const cmsCIELab* lab, double n[3]) { n[0] = lab->L / 100; n[1] = (lab->a + 128) / 255; n[2] = (lab->b + 128) / 255; }
static void NToLab(const double n[3], cmsCIELab* lab) { lab->L = n[0] * 100; lab->a = n[1] * 255 - 128; lab->b = n[2] * 255 - 128; }

static cmsHPROFILE NewProfile(double version, cmsColorSpaceSignature space, cmsColorSpaceSignature pcs) {
  cmsHPROFILE h = cmsCreateProfilePlaceholder(NULL);
  cmsSetProfileVersion(h, version); cmsSetDeviceClass(h, cmsSigDisplayClass);
  cmsSetColorSpace(h, space); cmsSetPCS(h, pcs); cmsSetHeaderRenderingIntent(h, INTENT_PERCEPTUAL);
  return h; }
static void Save(cmsHPROFILE h, cmsPipeline* a2b, cmsPipeline* b2a, const char* desc, const char* path) {
  cmsWriteTag(h, cmsSigAToB0Tag, a2b); cmsWriteTag(h, cmsSigBToA0Tag, b2a);
  cmsMLU* d = cmsMLUalloc(NULL, 1); cmsMLUsetASCII(d, "en", "US", desc); cmsWriteTag(h, cmsSigProfileDescriptionTag, d);
  cmsMLU* c = cmsMLUalloc(NULL, 1); cmsMLUsetASCII(c, "en", "US", "No copyright, use freely"); cmsWriteTag(h, cmsSigCopyrightTag, c);
  cmsWriteTag(h, cmsSigMediaWhitePointTag, cmsD50_XYZ());
  printf(cmsSaveProfileToFile(h, path) ? "wrote %s\n" : "FAILED %s\n", path);
  cmsCloseProfile(h); }
static void Add(cmsPipeline* p, cmsStage* s) { cmsPipelineInsertStage(p, cmsAT_END, s); }
static cmsStage* Curves(cmsToneCurve* a, cmsToneCurve* b, cmsToneCurve* c) { cmsToneCurve* t[3] = { a, b, c }; return cmsStageAllocToneCurves(NULL, 3, t); }

/* ---- RgbLut8-v2.icc: lut8 (mft1), version 2.1, Lab PCS ----
   A2B0: input tables x^2, 9-point CLUT, identity output tables.
   B2A0: identity input tables, 9-point CLUT, output tables y^(1/2.2). */
static cmsInt32Number Lut8A2B(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double rgb[3], n[3]; cmsCIELab lab;
  for (int i = 0; i < 3; i++) rgb[i] = pow(In[i] / 65535.0, 1 / 2.0);
  cmsDoTransform(rgbToLab, rgb, &lab, 1); LabToN(&lab, n);
  for (int i = 0; i < 3; i++) Out[i] = Word(n[i]);
  return 1; }
static cmsInt32Number Lut8B2A(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double n[3], rgb[3]; cmsCIELab lab;
  for (int i = 0; i < 3; i++) n[i] = In[i] / 65535.0;
  NToLab(n, &lab); cmsDoTransform(labToRgb, &lab, rgb, 1);
  for (int i = 0; i < 3; i++) Out[i] = Word(pow(Clip(rgb[i]), 2.2));
  return 1; }

/* ---- RgbLut16-v2.icc: lut16 (mft2), version 2.1, XYZ PCS ----
   A2B0: input tables x^2.2 (1024 entries), 11-point CLUT, output tables v^0.95 (4096 entries).
   B2A0: a 3x3 matrix on the XYZ input, input tables v^0.7 (512 entries), 11-point CLUT, output tables y^(1/2.2). */
static const double L16Mat[9] = { 0.90, 0.08, 0.02, 0.05, 0.85, 0.10, 0.01, 0.04, 0.95 };
static double L16MatInv[9];
static cmsInt32Number Lut16A2B(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double rgb[3], n[3];
  for (int i = 0; i < 3; i++) rgb[i] = pow(In[i] / 65535.0, 1 / 2.2);
  RgbToXyzN(rgb, n);
  for (int i = 0; i < 3; i++) Out[i] = Word(pow(Clip(n[i]), 1 / 0.95));
  return 1; }
static cmsInt32Number Lut16B2A(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double u[3], n[3], rgb[3];
  for (int i = 0; i < 3; i++) u[i] = pow(In[i] / 65535.0, 1 / 0.7);
  Mul3(L16MatInv, u, n); XyzNToRgb(n, rgb);
  for (int i = 0; i < 3; i++) Out[i] = Word(pow(Clip(rgb[i]), 2.2));
  return 1; }

/* ---- RgbLutAToB-v4.icc: lutAToBType / lutBToAType, version 4.3, XYZ PCS, every element ----
   A2B0: A curves, CLUT with 5 x 7 x 9 points, M curves, matrix with offset, B curves.
   B2A0: B curves, matrix with offset, M curves, CLUT with 9 x 7 x 5 points, A curves. */
static cmsToneCurve *AbA[3], *AbM[3], *AbB[3], *BaB[3], *BaM[3], *BaA[3];
static double AbMat[9], AbMatInv[9], BaMat[9], BaMatInv[9];
static double AbOff[3], BaOff[3] = { 0.002, 0.001, 0.0015 };
static cmsInt32Number AbA2B(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double x[3], n[3], b[3], lin[3];
  for (int i = 0; i < 3; i++) x[i] = EvalInv(AbA[i], In[i] / 65535.0);
  RgbToXyzN(x, n);
  for (int i = 0; i < 3; i++) b[i] = EvalInv(AbB[i], n[i]) - AbOff[i];
  Mul3(AbMatInv, b, lin);
  for (int i = 0; i < 3; i++) Out[i] = Word(EvalInv(AbM[i], lin[i]));
  return 1; }
static cmsInt32Number BaB2A(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double m[3], b[3], n[3], rgb[3];
  for (int i = 0; i < 3; i++) m[i] = EvalInv(BaM[i], In[i] / 65535.0) - BaOff[i];
  Mul3(BaMatInv, m, b);
  for (int i = 0; i < 3; i++) n[i] = EvalInv(BaB[i], b[i]);
  XyzNToRgb(n, rgb);
  for (int i = 0; i < 3; i++) Out[i] = Word(EvalInv(BaA[i], rgb[i]));
  return 1; }

/* ---- GrayLut-v4.icc: lutAToBType / lutBToAType, version 4.3, gray, Lab PCS ----
   A2B0: A curve x^1.5, CLUT with 17 points (1 input, 3 outputs), identity B curves.
   B2A0: B curves (L* gamma 1.1, identity a* and b*), CLUT with 9 x 9 x 9 points (3 inputs, 1 output), A curve gamma 0.9. */
static cmsToneCurve *GrA, *GrB[3], *GrOutA;
static cmsInt32Number GrayA2B(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double g = EvalInv(GrA, In[0] / 65535.0), n[3]; cmsCIELab lab;
  cmsDoTransform(grayToLab, &g, &lab, 1); LabToN(&lab, n);
  for (int i = 0; i < 3; i++) Out[i] = Word(n[i]);
  return 1; }
static cmsInt32Number GrayB2A(const cmsUInt16Number In[], cmsUInt16Number Out[], void* c) {
  double n[3], g; cmsCIELab lab;
  for (int i = 0; i < 3; i++) n[i] = EvalInv(GrB[i], In[i] / 65535.0);
  NToLab(n, &lab); cmsDoTransform(labToGray, &lab, &g, 1);
  Out[0] = Word(EvalInv(GrOutA, g));
  return 1; }

int main(void) {
  cmsCIExyY d65 = { 0.3127, 0.3290, 1.0 };
  cmsCIExyYTRIPLE adobe = { { 0.64, 0.33, 1 }, { 0.21, 0.71, 1 }, { 0.15, 0.06, 1 } };
  cmsToneCurve* g22 = Gamma(563.0 / 256.0);
  cmsToneCurve* trc[3] = { g22, g22, g22 };
  cmsHPROFILE hRgb = cmsCreateRGBProfile(&d65, &adobe, trc), hGray = cmsCreateGrayProfile(cmsD50_xyY(), g22);
  cmsHPROFILE hLab = cmsCreateLab4Profile(NULL), hXyz = cmsCreateXYZProfile();
  cmsUInt32Number f = cmsFLAGS_NOOPTIMIZE | cmsFLAGS_NOCACHE, rel = INTENT_RELATIVE_COLORIMETRIC;
  rgbToXyz = cmsCreateTransform(hRgb, TYPE_RGB_DBL, hXyz, TYPE_XYZ_DBL, rel, f);
  xyzToRgb = cmsCreateTransform(hXyz, TYPE_XYZ_DBL, hRgb, TYPE_RGB_DBL, rel, f);
  rgbToLab = cmsCreateTransform(hRgb, TYPE_RGB_DBL, hLab, TYPE_Lab_DBL, rel, f);
  labToRgb = cmsCreateTransform(hLab, TYPE_Lab_DBL, hRgb, TYPE_RGB_DBL, rel, f);
  grayToLab = cmsCreateTransform(hGray, TYPE_GRAY_DBL, hLab, TYPE_Lab_DBL, rel, f);
  labToGray = cmsCreateTransform(hLab, TYPE_Lab_DBL, hGray, TYPE_GRAY_DBL, rel, f);
  cmsUInt32Number g9[3] = { 9, 9, 9 }, g11[3] = { 11, 11, 11 }, g579[3] = { 5, 7, 9 }, g975[3] = { 9, 7, 5 }, g17[1] = { 17 };

  /* lut8 */
  cmsPipeline* a2b = cmsPipelineAlloc(NULL, 3, 3); cmsStage* clut = cmsStageAllocCLut16bitGranular(NULL, g9, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, Lut8A2B, NULL, 0);
  Add(a2b, Curves(Table(256, 2.0), Table(256, 2.0), Table(256, 2.0))); Add(a2b, clut); Add(a2b, Curves(Gamma(1), Gamma(1), Gamma(1)));
  cmsPipeline* b2a = cmsPipelineAlloc(NULL, 3, 3); clut = cmsStageAllocCLut16bitGranular(NULL, g9, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, Lut8B2A, NULL, 0);
  Add(b2a, Curves(Gamma(1), Gamma(1), Gamma(1))); Add(b2a, clut); Add(b2a, Curves(Table(256, 1 / 2.2), Table(256, 1 / 2.2), Table(256, 1 / 2.2)));
  cmsPipelineSetSaveAs8bitsFlag(a2b, TRUE); cmsPipelineSetSaveAs8bitsFlag(b2a, TRUE);
  Save(NewProfile(2.1, cmsSigRgbData, cmsSigLabData), a2b, b2a, "Synthetic RGB lut8 (Little CMS), test only", "RgbLut8-v2.icc");

  /* lut16 */
  Invert3(L16Mat, L16MatInv);
  a2b = cmsPipelineAlloc(NULL, 3, 3); clut = cmsStageAllocCLut16bitGranular(NULL, g11, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, Lut16A2B, NULL, 0);
  Add(a2b, Curves(Table(1024, 2.2), Table(1024, 2.2), Table(1024, 2.2))); Add(a2b, clut); Add(a2b, Curves(Table(4096, 0.95), Table(4096, 0.95), Table(4096, 0.95)));
  b2a = cmsPipelineAlloc(NULL, 3, 3); clut = cmsStageAllocCLut16bitGranular(NULL, g11, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, Lut16B2A, NULL, 0);
  Add(b2a, cmsStageAllocMatrix(NULL, 3, 3, L16Mat, NULL));
  Add(b2a, Curves(Table(512, 0.7), Table(512, 0.7), Table(512, 0.7))); Add(b2a, clut); Add(b2a, Curves(Table(4096, 1 / 2.2), Table(4096, 1 / 2.2), Table(4096, 1 / 2.2)));
  Save(NewProfile(2.1, cmsSigRgbData, cmsSigXYZData), a2b, b2a, "Synthetic RGB lut16 (Little CMS), test only", "RgbLut16-v2.icc");

  /* mAB / mBA */
  cmsCIEXYZ* r = cmsReadTag(hRgb, cmsSigRedColorantTag); cmsCIEXYZ* g = cmsReadTag(hRgb, cmsSigGreenColorantTag); cmsCIEXYZ* b = cmsReadTag(hRgb, cmsSigBlueColorantTag);
  double colorants[9] = { r->X * Enc, g->X * Enc, b->X * Enc, r->Y * Enc, g->Y * Enc, b->Y * Enc, r->Z * Enc, g->Z * Enc, b->Z * Enc };
  for (int i = 0; i < 9; i++) { AbMat[i] = colorants[i]; BaMatInv[i] = colorants[i]; }
  Invert3(AbMat, AbMatInv); Invert3(BaMatInv, BaMat);
  double lift[3] = { 0.002, 0.002, 0.002 }; Mul3(AbMat, lift, AbOff); for (int i = 0; i < 3; i++) AbOff[i] = -AbOff[i];
  AbA[0] = Gamma(2.2); AbA[1] = SrgbCurve(); AbA[2] = Table(256, 2.0);
  AbM[0] = Gamma(0.8); AbM[1] = Gamma(1.0); AbM[2] = Table(128, 1.25);
  AbB[0] = Gamma(1.0); AbB[1] = Gamma(1.02); AbB[2] = Table(64, 0.98);
  a2b = cmsPipelineAlloc(NULL, 3, 3); clut = cmsStageAllocCLut16bitGranular(NULL, g579, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, AbA2B, NULL, 0);
  Add(a2b, Curves(AbA[0], AbA[1], AbA[2])); Add(a2b, clut); Add(a2b, Curves(AbM[0], AbM[1], AbM[2]));
  Add(a2b, cmsStageAllocMatrix(NULL, 3, 3, AbMat, AbOff)); Add(a2b, Curves(AbB[0], AbB[1], AbB[2]));
  BaB[0] = Gamma(1.0); BaB[1] = Gamma(0.97); BaB[2] = Table(256, 1.0);
  BaM[0] = Table(256, 0.9); BaM[1] = Gamma(1.1); BaM[2] = Gamma(1.0);
  BaA[0] = Gamma(1 / 2.2); BaA[1] = Table(1024, 1 / 2.0); BaA[2] = SrgbInverse();
  b2a = cmsPipelineAlloc(NULL, 3, 3); clut = cmsStageAllocCLut16bitGranular(NULL, g975, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, BaB2A, NULL, 0);
  Add(b2a, Curves(BaB[0], BaB[1], BaB[2])); Add(b2a, cmsStageAllocMatrix(NULL, 3, 3, BaMat, BaOff));
  Add(b2a, Curves(BaM[0], BaM[1], BaM[2])); Add(b2a, clut); Add(b2a, Curves(BaA[0], BaA[1], BaA[2]));
  Save(NewProfile(4.3, cmsSigRgbData, cmsSigXYZData), a2b, b2a, "Synthetic RGB lutAToB/lutBToA (Little CMS), test only", "RgbLutAToB-v4.icc");

  /* gray mAB / mBA */
  GrA = Table(256, 1.5); GrB[0] = Gamma(1.1); GrB[1] = Gamma(1.0); GrB[2] = Gamma(1.0); GrOutA = Gamma(0.9);
  a2b = cmsPipelineAlloc(NULL, 1, 3); clut = cmsStageAllocCLut16bitGranular(NULL, g17, 1, 3, NULL);
  cmsStageSampleCLut16bit(clut, GrayA2B, NULL, 0);
  Add(a2b, cmsStageAllocToneCurves(NULL, 1, &GrA)); Add(a2b, clut); Add(a2b, Curves(Gamma(1), Gamma(1), Gamma(1)));
  b2a = cmsPipelineAlloc(NULL, 3, 1); clut = cmsStageAllocCLut16bitGranular(NULL, g9, 3, 1, NULL);
  cmsStageSampleCLut16bit(clut, GrayB2A, NULL, 0);
  Add(b2a, Curves(GrB[0], GrB[1], GrB[2])); Add(b2a, clut); Add(b2a, cmsStageAllocToneCurves(NULL, 1, &GrOutA));
  Save(NewProfile(4.3, cmsSigGrayData, cmsSigLabData), a2b, b2a, "Synthetic gray lutAToB/lutBToA (Little CMS), test only", "GrayLut-v4.icc");
  return 0; }
