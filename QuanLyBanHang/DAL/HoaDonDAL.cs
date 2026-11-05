using System.Data;
using Microsoft.Data.SqlClient;
using QuanLyBanHang.DTO;

namespace QuanLyBanHang.DAL
{
    public class HoaDonDAL
    {
        /// <summary>
        /// Lập hóa đơn trong MỘT SqlTransaction (ACID):
        ///  1. INSERT HoaDon  2. INSERT ChiTietHoaDon  3. Trừ SoLuongTon
        ///  4. Cộng DiemTichLuy  5. Commit. Bất kỳ bước nào lỗi → Rollback toàn bộ.
        /// </summary>
        /// <returns>Mã hóa đơn vừa tạo.</returns>
        public int LapHoaDon(HoaDonDTO hd, int diemCong)
        {
            using var conn = DBConnection.GetConnection();
            conn.Open();
            SqlTransaction trans = conn.BeginTransaction();
            try
            {
                // 1. Chèn hóa đơn, lấy MaHD vừa sinh
                int maHD;
                using (var cmd = new SqlCommand(
                    "INSERT INTO HoaDon (NgayLap, MaNV, MaKH, TongTien) VALUES (GETDATE(), @nv, @kh, @tong); " +
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans))
                {
                    cmd.Parameters.Add("@nv", SqlDbType.Int).Value = hd.MaNV;
                    cmd.Parameters.Add("@kh", SqlDbType.Int).Value = (object)hd.MaKH ?? DBNull.Value;
                    var p = cmd.Parameters.Add("@tong", SqlDbType.Decimal);
                    p.Precision = 18; p.Scale = 2; p.Value = hd.TongTien;
                    maHD = (int)cmd.ExecuteScalar();
                }

                foreach (var ct in hd.ChiTiet)
                {
                    // 2. Chèn chi tiết hóa đơn
                    using (var cmd = new SqlCommand(
                        "INSERT INTO ChiTietHoaDon (MaHD, MaSP, SoLuong, DonGia) VALUES (@hd, @sp, @sl, @gia)", conn, trans))
                    {
                        cmd.Parameters.Add("@hd", SqlDbType.Int).Value = maHD;
                        cmd.Parameters.Add("@sp", SqlDbType.VarChar, 20).Value = ct.MaSP;
                        cmd.Parameters.Add("@sl", SqlDbType.Int).Value = ct.SoLuong;
                        var p = cmd.Parameters.Add("@gia", SqlDbType.Decimal);
                        p.Precision = 18; p.Scale = 2; p.Value = ct.DonGia;
                        cmd.ExecuteNonQuery();
                    }

                    // 3. Trừ tồn kho – chỉ trừ khi còn đủ hàng
                    using (var cmd = new SqlCommand(
                        "UPDATE SanPham SET SoLuongTon = SoLuongTon - @sl WHERE MaSP = @sp AND SoLuongTon >= @sl", conn, trans))
                    {
                        cmd.Parameters.Add("@sl", SqlDbType.Int).Value = ct.SoLuong;
                        cmd.Parameters.Add("@sp", SqlDbType.VarChar, 20).Value = ct.MaSP;
                        if (cmd.ExecuteNonQuery() == 0)
                        {
                            // SoLuongTon < SoLuong bán → hủy toàn bộ giao dịch
                            trans.Rollback();
                            throw new GiaoDichException(
                                $"Sản phẩm \"{ct.TenSP}\" ({ct.MaSP}) không đủ tồn kho để bán {ct.SoLuong}.\n" +
                                "Giao dịch đã được ROLLBACK – không có dữ liệu nào bị ghi.");
                        }
                    }
                }

                // 4. Cộng điểm tích lũy
                if (hd.MaKH.HasValue && diemCong > 0)
                {
                    using var cmd = new SqlCommand(
                        "UPDATE KhachHang SET DiemTichLuy = DiemTichLuy + @diem WHERE MaKH = @kh", conn, trans);
                    cmd.Parameters.Add("@diem", SqlDbType.Int).Value = diemCong;
                    cmd.Parameters.Add("@kh", SqlDbType.Int).Value = hd.MaKH.Value;
                    cmd.ExecuteNonQuery();
                }

                // 5. Hoàn tất
                trans.Commit();
                return maHD;
            }
            catch (GiaoDichException)
            {
                throw; // đã rollback ở trên
            }
            catch (Exception ex)
            {
                try { trans.Rollback(); } catch { /* kết nối đã đóng */ }
                throw new Exception("Lỗi khi lập hóa đơn, giao dịch đã ROLLBACK: " + ex.Message, ex);
            }
        }

        public DataTable LayDanhSach()
        {
            using var conn = DBConnection.GetConnection();
            using var da = new SqlDataAdapter(
                "SELECT hd.MaHD, hd.NgayLap, nv.TenNV, ISNULL(kh.TenKH, N'Khách lẻ') AS KhachHang, hd.TongTien " +
                "FROM HoaDon hd JOIN NhanVien nv ON hd.MaNV = nv.MaNV LEFT JOIN KhachHang kh ON hd.MaKH = kh.MaKH " +
                "ORDER BY hd.MaHD DESC", conn);
            var dt = new DataTable();
            da.Fill(dt);
            return dt;
        }
    }

    /// <summary>Lỗi nghiệp vụ trong giao dịch (đã Rollback).</summary>
    public class GiaoDichException : Exception
    {
        public GiaoDichException(string msg) : base(msg) { }
    }
}
