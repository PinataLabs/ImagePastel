/* Based on Little CMS testbed/testcms2.c CreateFakeCMYK (MIT, Marti Maria). Writes a synthetic LUT CMYK profile. */
#include "lcms2.h"
#include <stdio.h>
typedef struct { cmsHTRANSFORM hLab2sRGB, sRGB2Lab, hIlimit; } P;
static double Clip(double v){ return v<0?0:(v>1?1:v); }
static cmsInt32Number Fwd(const cmsUInt16Number In[], cmsUInt16Number Out[], void* C){
  P* p=(P*)C; double rgb[3], cmyk[4]; cmsDoTransform(p->hLab2sRGB, In, rgb, 1);
  double c=1-rgb[0], m=1-rgb[1], y=1-rgb[2]; double k = c<m ? (c<y?c:y) : (m<y?m:y);
  cmyk[0]=c; cmyk[1]=m; cmyk[2]=y; cmyk[3]=k; cmsDoTransform(p->hIlimit, cmyk, Out, 1); return 1; }
static cmsInt32Number Rev(const cmsUInt16Number In[], cmsUInt16Number Out[], void* C){
  P* p=(P*)C; double c=In[0]/65535.0, m=In[1]/65535.0, y=In[2]/65535.0, k=In[3]/65535.0, rgb[3];
  rgb[0]=Clip((1-c)*(1-k)); rgb[1]=Clip((1-m)*(1-k)); rgb[2]=Clip((1-y)*(1-k));
  cmsDoTransform(p->sRGB2Lab, rgb, Out, 1); return 1; }
static void Err(cmsContext c, cmsUInt32Number code, const char* t){ printf("lcms error %u: %s%c", code, t, 10); }
int main(int argc, char** argv){ cmsSetLogErrorHandler(Err);
  const char* out = argc>1?argv[1]:"fake-cmyk-lut.icc";
  cmsHPROFILE hsRGB=cmsCreate_sRGBProfile(), hLab=cmsCreateLab4Profile(NULL), hLimit=cmsCreateInkLimitingDeviceLink(cmsSigCmykData, 300);
  P p; cmsUInt32Number cmykfrm = FLOAT_SH(1)|BYTES_SH(0)|CHANNELS_SH(4);
  p.hLab2sRGB=cmsCreateTransform(hLab, TYPE_Lab_16, hsRGB, TYPE_RGB_DBL, INTENT_PERCEPTUAL, cmsFLAGS_NOOPTIMIZE|cmsFLAGS_NOCACHE);
  p.sRGB2Lab=cmsCreateTransform(hsRGB, TYPE_RGB_DBL, hLab, TYPE_Lab_16, INTENT_PERCEPTUAL, cmsFLAGS_NOOPTIMIZE|cmsFLAGS_NOCACHE);
  p.hIlimit=cmsCreateTransform(hLimit, cmykfrm, NULL, TYPE_CMYK_16, INTENT_PERCEPTUAL, cmsFLAGS_NOOPTIMIZE|cmsFLAGS_NOCACHE);
  cmsHPROFILE h=cmsCreateProfilePlaceholder(0);
  cmsSetProfileVersion(h, 4.3); cmsSetDeviceClass(h, cmsSigOutputClass); cmsSetColorSpace(h, cmsSigCmykData); cmsSetPCS(h, cmsSigLabData);
  cmsSetHeaderRenderingIntent(h, INTENT_PERCEPTUAL);
  cmsPipeline* A2B=cmsPipelineAlloc(0,4,3); cmsStage* cl=cmsStageAllocCLut16bit(0,17,4,3,NULL);
  cmsStageSampleCLut16bit(cl, Rev, &p, 0); cmsPipelineInsertStage(A2B, cmsAT_END, cmsStageAllocToneCurves(0,4,NULL)); cmsPipelineInsertStage(A2B, cmsAT_END, cl); cmsPipelineInsertStage(A2B, cmsAT_END, cmsStageAllocToneCurves(0,3,NULL));
  cmsPipeline* B2A=cmsPipelineAlloc(0,3,4); cl=cmsStageAllocCLut16bit(0,17,3,4,NULL);
  cmsStageSampleCLut16bit(cl, Fwd, &p, 0); cmsPipelineInsertStage(B2A, cmsAT_END, cmsStageAllocToneCurves(0,3,NULL)); cmsPipelineInsertStage(B2A, cmsAT_END, cl); cmsPipelineInsertStage(B2A, cmsAT_END, cmsStageAllocToneCurves(0,4,NULL));
  cmsWriteTag(h, cmsSigAToB0Tag, A2B); cmsWriteTag(h, cmsSigBToA0Tag, B2A); cmsLinkTag(h, cmsSigAToB1Tag, cmsSigAToB0Tag); cmsLinkTag(h, cmsSigBToA1Tag, cmsSigBToA0Tag);
  cmsMLU* d=cmsMLUalloc(0,1); cmsMLUsetASCII(d,"en","US","Synthetic CMYK (lcms CreateFakeCMYK), test only"); cmsWriteTag(h, cmsSigProfileDescriptionTag, d);
  cmsMLU* c=cmsMLUalloc(0,1); cmsMLUsetASCII(c,"en","US","No copyright, use freely"); cmsWriteTag(h, cmsSigCopyrightTag, c);
  cmsWriteTag(h, cmsSigMediaWhitePointTag, cmsD50_XYZ());
  if(!cmsSaveProfileToFile(h, out)){ printf("save failed\n"); return 1; }
  /* sanity: CMYK -> sRGB */
  cmsHPROFILE hc=cmsOpenProfileFromFile(out,"r"); cmsHTRANSFORM t=cmsCreateTransform(hc, TYPE_CMYK_8, hsRGB, TYPE_RGB_8, INTENT_PERCEPTUAL, 0);
  cmsUInt8Number in[4][4]={{0,0,0,0},{255,0,0,0},{0,0,0,255},{128,64,0,32}}, o[3];
  for(int i=0;i<4;i++){ cmsDoTransform(t,in[i],o,1); printf("CMYK %3d %3d %3d %3d -> RGB %3d %3d %3d\n", in[i][0],in[i][1],in[i][2],in[i][3],o[0],o[1],o[2]); }
  return 0; }
