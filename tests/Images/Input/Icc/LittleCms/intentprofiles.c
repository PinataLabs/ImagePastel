/* Copyright (c) PinataLabs.
   Licensed under the Apache License, Version 2.0. */
/* Writes synthetic test profiles for rendering intents and black-point compensation with
   Little CMS 2 (MIT). Each intent's tables describe a different variant of an Adobe RGB (1998)-like
   space: its own tone curve, a lifted black (a lighter black point) and a chroma scale, so the
   tables differ per intent and the black points differ between profiles. The media white point
   is a paper white, not D50, so absolute colorimetric differs from relative colorimetric. */
#include "lcms2.h"
#include <math.h>
#include <stdio.h>

static double Clip(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
static cmsUInt16Number Word(double v) { return (cmsUInt16Number) floor(Clip(v) * 65535.0 + 0.5); }
static void LabToN(const cmsCIELab* lab, double n[3]) { n[0] = lab->L / 100; n[1] = (lab->a + 128) / 255; n[2] = (lab->b + 128) / 255; }
static void NToLab(const double n[3], cmsCIELab* lab) { lab->L = n[0] * 100; lab->a = n[1] * 255 - 128; lab->b = n[2] * 255 - 128; }

static cmsHTRANSFORM rgbToLab, labToRgb;

/* One intent's variant: device value v -> linear light lift + (1 - lift) * v^gamma in the gamma 2.2
   Adobe-like space, then a* and b* scaled by chroma. */
typedef struct { double gamma, lift, chroma; } Variant;

static cmsInt32Number A2B(const cmsUInt16Number In[], cmsUInt16Number Out[], void* cargo) {
  const Variant* v = (const Variant*) cargo; double rgb[3], n[3]; cmsCIELab lab;
  for (int i = 0; i < 3; i++) rgb[i] = pow(v->lift + (1 - v->lift) * pow(In[i] / 65535.0, v->gamma), 1 / 2.2);
  cmsDoTransform(rgbToLab, rgb, &lab, 1);
  lab.a *= v->chroma; lab.b *= v->chroma;
  LabToN(&lab, n);
  for (int i = 0; i < 3; i++) Out[i] = Word(n[i]);
  return 1; }
static cmsInt32Number B2A(const cmsUInt16Number In[], cmsUInt16Number Out[], void* cargo) {
  const Variant* v = (const Variant*) cargo; double n[3], rgb[3]; cmsCIELab lab;
  for (int i = 0; i < 3; i++) n[i] = In[i] / 65535.0;
  NToLab(n, &lab); lab.a /= v->chroma; lab.b /= v->chroma;
  cmsDoTransform(labToRgb, &lab, rgb, 1);
  for (int i = 0; i < 3; i++) Out[i] = Word(pow(Clip((pow(Clip(rgb[i]), 2.2) - v->lift) / (1 - v->lift)), 1 / v->gamma));
  return 1; }

static cmsStage* Identity(void) {
  cmsToneCurve* c = cmsBuildGamma(NULL, 1.0); cmsToneCurve* t[3] = { c, c, c };
  cmsStage* s = cmsStageAllocToneCurves(NULL, 3, t); cmsFreeToneCurve(c); return s; }
static cmsPipeline* Table(cmsSAMPLER16 sampler, const Variant* v) {
  cmsUInt32Number grid[3] = { 11, 11, 11 };
  cmsPipeline* p = cmsPipelineAlloc(NULL, 3, 3);
  cmsStage* clut = cmsStageAllocCLut16bitGranular(NULL, grid, 3, 3, NULL);
  cmsStageSampleCLut16bit(clut, sampler, (void*) v, 0);
  cmsPipelineInsertStage(p, cmsAT_END, Identity()); cmsPipelineInsertStage(p, cmsAT_END, clut); cmsPipelineInsertStage(p, cmsAT_END, Identity());
  return p; }
static void WriteIntent(cmsHPROFILE h, int intent, const Variant* v) {
  static const cmsTagSignature a2b[3] = { cmsSigAToB0Tag, cmsSigAToB1Tag, cmsSigAToB2Tag };
  static const cmsTagSignature b2a[3] = { cmsSigBToA0Tag, cmsSigBToA1Tag, cmsSigBToA2Tag };
  cmsPipeline* p = Table(A2B, v); cmsWriteTag(h, a2b[intent], p); cmsPipelineFree(p);
  p = Table(B2A, v); cmsWriteTag(h, b2a[intent], p); cmsPipelineFree(p); }

static const cmsCIEXYZ Paper = { 0.9000, 0.9300, 0.7300 };

static void Describe(cmsHPROFILE h, const char* desc) {
  cmsMLU* d = cmsMLUalloc(NULL, 1); cmsMLUsetASCII(d, "en", "US", desc); cmsWriteTag(h, cmsSigProfileDescriptionTag, d); cmsMLUfree(d);
  cmsMLU* c = cmsMLUalloc(NULL, 1); cmsMLUsetASCII(c, "en", "US", "No copyright, use freely"); cmsWriteTag(h, cmsSigCopyrightTag, c); cmsMLUfree(c); }
static cmsHPROFILE NewLutProfile(double version) {
  cmsHPROFILE h = cmsCreateProfilePlaceholder(NULL);
  cmsSetProfileVersion(h, version); cmsSetDeviceClass(h, cmsSigOutputClass);
  cmsSetColorSpace(h, cmsSigRgbData); cmsSetPCS(h, cmsSigLabData); cmsSetHeaderRenderingIntent(h, INTENT_PERCEPTUAL);
  cmsWriteTag(h, cmsSigMediaWhitePointTag, &Paper);
  return h; }
static void Save(cmsHPROFILE h, const char* path) {
  printf(cmsSaveProfileToFile(h, path) ? "wrote %s\n" : "FAILED %s\n", path); cmsCloseProfile(h); }

int main(void) {
  cmsCIExyY d65 = { 0.3127, 0.3290, 1.0 };
  cmsCIExyYTRIPLE adobe = { { 0.64, 0.33, 1 }, { 0.21, 0.71, 1 }, { 0.15, 0.06, 1 } };
  cmsToneCurve* g22 = cmsBuildGamma(NULL, 2.2);
  cmsToneCurve* trc[3] = { g22, g22, g22 };
  cmsHPROFILE hRgb = cmsCreateRGBProfile(&d65, &adobe, trc), hLab = cmsCreateLab4Profile(NULL);
  cmsUInt32Number f = cmsFLAGS_NOOPTIMIZE | cmsFLAGS_NOCACHE, rel = INTENT_RELATIVE_COLORIMETRIC;
  rgbToLab = cmsCreateTransform(hRgb, TYPE_RGB_DBL, hLab, TYPE_Lab_DBL, rel, f);
  labToRgb = cmsCreateTransform(hLab, TYPE_Lab_DBL, hRgb, TYPE_RGB_DBL, rel, f);

  const Variant perceptual = { 1.8, 0.0, 0.85 }, relative = { 2.2, 0.02, 1.0 }, saturation = { 2.0, 0.0, 1.3 };
  const Variant matrixRelative = { 2.4, 0.03, 0.9 };

  /* RgbIntents-v2.icc: output class, version 2.1, Lab PCS, lut16 A2B0/A2B1/A2B2 and B2A0/B2A1/B2A2. */
  cmsHPROFILE h = NewLutProfile(2.1);
  WriteIntent(h, 0, &perceptual); WriteIntent(h, 1, &relative); WriteIntent(h, 2, &saturation);
  Describe(h, "Synthetic RGB, three intents, lut16 (Little CMS), test only");
  Save(h, "RgbIntents-v2.icc");

  /* RgbIntents-v4.icc: the same perceptual and relative tables, version 4.3 (lutAToBType and
     lutBToAType), and no saturation tables. */
  h = NewLutProfile(4.3);
  WriteIntent(h, 0, &perceptual); WriteIntent(h, 1, &relative);
  Describe(h, "Synthetic RGB, perceptual and relative intents, lutAToB (Little CMS), test only");
  Save(h, "RgbIntents-v4.icc");

  /* RgbIntentsMatrix-v2.icc: display class, version 2.1, matrix/TRC tags whose curves lift black
     (ICC parametric type 1, Y = (aX + b)^2.2 with b^2.2 = 0.01 and a + b = 1), plus only A2B1/B2A1 (a
     different variant). As a
     version 2 display profile its media white is D50 to Little CMS, whatever its wtpt tag says. */
  double offset = pow(0.01, 1 / 2.2);
  cmsFloat64Number lifted[3] = { 2.2, 1 - offset, offset };
  cmsToneCurve* lift = cmsBuildParametricToneCurve(NULL, 2, lifted);
  cmsToneCurve* liftTrc[3] = { lift, lift, lift };
  h = cmsCreateRGBProfile(&d65, &adobe, liftTrc);
  cmsSetProfileVersion(h, 2.1); cmsSetDeviceClass(h, cmsSigDisplayClass);
  cmsWriteTag(h, cmsSigMediaWhitePointTag, &Paper);
  WriteIntent(h, 1, &matrixRelative);
  Describe(h, "Synthetic RGB, matrix/TRC and relative intent tables (Little CMS), test only");
  Save(h, "RgbIntentsMatrix-v2.icc");
  return 0; }
