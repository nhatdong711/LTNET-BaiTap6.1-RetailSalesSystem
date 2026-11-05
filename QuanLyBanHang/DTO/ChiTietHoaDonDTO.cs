namespace QuanLyBanHang.DTO
{
    public class ChiTietHoaDonDTO
    {
        public int MaHD { get; set; }
        public string MaSP { get; set; }
        public string TenSP { get; set; }   // chỉ để hiển thị trên giỏ hàng
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien => SoLuong * DonGia;
    }
}
