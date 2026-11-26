export const podeAcessarBiBml011016 = (
  effective: { isMasterAdmin?: boolean } | null | undefined,
  loading = false,
  error: string | null = null,
) => !loading && !error && effective?.isMasterAdmin === true;
