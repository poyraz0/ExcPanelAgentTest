namespace ExcPanel.TransferAgent.Common;

public static class ByteSizeHelper
{
  private const double BytesPerGigabyte = 1024d * 1024d * 1024d;

  public static double ToGigabytes(long bytes) =>
      Math.Round(bytes / BytesPerGigabyte, 2);
}
