namespace ClinicManagement.IntegrationTests;

internal static class PatientEmergencyEvaluationCases
{
    internal static readonly string[] DevEmergency = """
        Bà nó thiếp đi giữa câu nói, gọi to mà chẳng buồn mở mắt.
        Toi gap nguoi di bo quy goi roi guc, ho im lim khong dap.
        Anh ấy ngừng nhúc nhích, hơi thở nghe đứt quãng rồi biến mất.
        Con nhỏ cố hút hơi vào, dưới cổ lõm sâu theo từng nhịp.
        Đang nhai thịt, cô ấy đưa cả hai tay lên cổ rồi chẳng nói được.
        Bau troi khong lanh nhung moi chau ngat xanh, chau dang tho yeu.
        Mắt em đảo lên, hai tay giật loạn và miệng sùi bọt.
        Lúc nãy con chạy nhảy, giờ toàn thân cứng, gọi tên không nhận ra.
        Cô tôi ói ra một cốc chất đỏ đặc lẫn thức ăn.
        Phân loãng kèm cả vũng máu, tôi đứng không vững.
        Vết cắt xuyên qua đùi, ép vải rồi vẫn thấy máu bắn.
        Tôi thay băng ba lần nhưng vết thương cứ ướt đẫm trong chốc lát.
        Lồng ngực tôi như bị kẹp lại, hàm cũng đau và tôi toát mồ hôi.
        Ong toi thay o giua nguc nang nhu co bao gao de.
        Bà đang cười thì bên mặt chùng xuống và giọng méo đi.
        Mới đây khi nhấc đồ ông thấy cánh tay trái rơi xuống, không nâng lại được.
        Nửa người phải tôi vừa mềm nhũn, miệng nói chẳng tròn chữ.
        Cổ tôi thắt dần sau khi ăn tôm, miệng phù và không lấy được hơi.
        Mật ong chưa ăn nhưng ong vừa chích, lưỡi tôi đã sưng lên và giọng tắt.
        Bé trút một lượng lớn thuốc nước của chú vào miệng.
        Em toi nuot het cho vien nam trong vi thuoc.
        Bà uống thêm mấy lần thuốc cùng buổi vì quên cả các lần trước.
        Cậu say, gọi tên và vỗ vào người cũng không mở mắt.
        Con nhặt nút pin đồ chơi và cho tuột hẳn xuống cổ.
        Bây giờ tôi mới thấy cái cúc áo đã mắc ở cổ con.
        Con nuốt mất mảnh nhựa văng ra từ chiếc xe đồ chơi vừa gãy.
        Cha tôi tưởng nước giải khát rồi uống dung dịch trong can giặt.
        Khu nhà bốc khói, vừa ra ngoài anh tôi đã thở rít và loạng choạng.
        Dầu rửa công nghiệp văng vào mắt, thị lực tụt hẳn trong chốc lát.
        Chú tôi ăn mấy cây nấm hái cạnh rừng rồi nôn không dừng.
        Trẻ vừa rơi xuống nước, khi kéo lên mặt tái xanh và không thấy ngực nhô lên.
        Sau khi va đầu vào bê tông, chị ấy chỉ ú ớ rồi thiếp đi.
        Đột ngột đau đầu khủng khiếp kèm nói ú ớ không tròn câu.
        Trẻ mới đẻ bỗng nằm rũ, da nóng, chỉ ngậm chứ không còn bú.
        Chị đang mang thai và ra nhiều máu tươi, người lâng lâng sắp đổ.
        Bị bỏng nước ở cả hai chân, da tróc thành từng mảng.
        Cô bị ong đốt xong ngã quỵ, người nổi đầy mảng đỏ.
        Hít hơi trong phòng sơn một lúc, đồng nghiệp bắt đầu lả, không tự đứng được.
        Đang ăn quả tròn thì bé trợn mắt, cố khóc mà không ra tiếng.
        Cháu uống nhầm thuốc, giờ mắt đảo và cứ giật tay chân.
        """.Split('\n');

    internal static readonly string[] DevRoutine = """
        Trước khi mở si rô ho có cần đọc hướng dẫn gì trên bao bì?
        Một bình thuốc còn lại sau lần điều trị trước được cất ở đâu?
        Bé đã ăn uống bình thường từ hôm qua, chỉ hỏi ngày tái khám.
        Tôi muốn hỏi tên các thuốc trên đơn đã cấp lần trước.
        Co thay duoc gia thuoc chong di ung qua mang khong?
        Nhà thuốc có mở vào ngày nghỉ để nhận thuốc theo đơn không?
        Đơn ghi dùng hai viên mỗi sáng, tôi có thể in lại chỉ dẫn không?
        Còn một nửa chai si rô, tôi muốn biết hạn sử dụng sau khi mở.
        Chỗ nào bán pin thay điều khiển?
        Giá của loại nam châm trang trí này có gồm vận chuyển không?
        Trường có buổi học về cách phòng tránh sặc khi ăn không?
        Hôm trước tôi xem tin tức về một vụ uống phải nước tẩy.
        Cách bảo quản bình chứa hóa chất vệ sinh để trẻ không với tới?
        Tôi muốn liên hệ với ban tổ chức lớp sơ cứu.
        Vật nhỏ để ngoài tầm tay, cháu chưa chạm vào.
        Bé không nuốt gì, tôi thấy hạt rơi nguyên dưới đất.
        Con làm rơi cúc xuống vực ghế, tôi vừa nhặt lên đây.
        Tôi vẫn thở tốt, chỉ muốn hỏi lịch kiểm tra họng định kỳ.
        Ba đã hết đau sau điều trị, nay muốn xem tên bác sĩ khám cho ba.
        Bà được xuất viện, đang nói năng bình thường và cần đi tái khám.
        Hôm kia bé bị sặc nhưng bác sĩ đã khám và nói ổn.
        Đang thay miếng dán cũ, vết thương đã khô, không có máu.
        Tôi chạy lên cầu thang, nghỉ chút rồi thở lại bình thường.
        Bao lau moi can tiem nhac phong uon van?
        Có đổi được hẹn tiêm từ hôm nay sang thứ bảy?
        Tiền khám cho trẻ tiêm phòng có bao gồm trong gói không?
        Sáng mai khu khám có điều hòa và chỗ ngồi đợi không?
        Mệt chết mất, tối có tiếp nhận khám tổng quát không?
        Ban dem o day giu xe may o dau?
        Ngứa bắp tay muốn đi khám, nên chọn chuyên khoa nào?
        Bác sĩ hẹn ba tuần sau kiểm tra sẹo đã lành.
        Tôi đi kiểm tra định kỳ vì gia đình có người bị đột quỵ từ năm ngoái.
        Tôi đọc một cuốn sách đề cập đến triệu chứng mất ý thức.
        Cháu chơi xong cất đủ viên bi vào hộp, vẫn ăn và tập thể dục đều.
        """.Split('\n');

    internal static readonly string[] TestEmergency = """
        Khi tôi tới nơi, em ngồi gục đầu, vỗ vào má cũng chẳng biết gì.
        Bác đang đứng cạnh quầy thì rã cả người, khuỵu xuống rồi không đáp.
        Từ lúc ngã, bạn tôi cứ liếc mắt vô định, hỏi gì như không nghe.
        Cậu nằm ngửa, tôi áp sát mà chẳng nghe thấy luồng hơi nào.
        Anh tôi rướn cổ hớp hơi, mới nhả được hai chữ đã phải dừng.
        Moi be xanh bam, tho cham den muc nhin khong thay bung dong day.
        Đang cắn miếng táo thì con tôi bỗng nắm cổ, hai mắt mở to mà không nói được.
        Một mẩu bánh vướng trong họng, bé quơ tay cầu cứu và hít vào không được.
        Me sac canh xong cha noi duoc, chi co tieng hit yeu.
        Hai hàm răng bỗng nghiến vào nhau rồi cả chân tay giãy từng nhịp.
        Cả người cháu rung giật, mắt lật ngược lên và không nghe gọi.
        Ông tôi người cứng như khúc gỗ, sau đó tay chân bật lên từng hồi.
        Cô tôi vừa ói một bát dịch màu nâu đen có hạt như bã cà phê.
        Mỗi lần đi cầu nước đỏ tràn bồn, tôi hoa cả mắt.
        Bé nôn phun, dịch có vệt máu đỏ tươi.
        Lớp vải mới đắp lên bắp chân lập tức đỏ ngấm, máu còn tuôn xuống đất.
        Co tho mo bi cat sau vao bap tay, ep gac ma mau van rao rao.
        Rách cổ tay và cứ mỗi nhịp mạch máu lại bật lên thành dòng.
        Đang nghỉ mà giữa ức nặng như ai ngồi đè, mồ hôi lạnh chảy ròng.
        Ngực đau nhói dội sang bả vai, ngồi xuống vẫn không đỡ.
        Bên môi bà xệ hẳn xuống, câu nói tự nhiên chỉ còn tiếng ú ớ.
        Ong buoc ra cua, chan ben phai bot dung duoc va tay roi thung.
        Đang uống nước bình thường bỗng một tay mất sức, miệng lại lệch sang một bên.
        Lưỡi bé choán cả miệng sau khi ăn, giờ bé cố thở mà giọng tắt dần.
        Sau mũi tiêm anh tôi nổi sẩn, cổ thít lại và đầu gối khuỵu xuống.
        Mặt tôi phù nhanh sau khi ăn hải sản, nói một câu cũng hụt hơi.
        Mới quay đi một lúc, cháu đã tu gần trọn chỗ si rô còn trong bình của người lớn.
        Vo toi do mot nho thuoc ra tay roi nuot het trong cung mot lan.
        Ba trót dùng cả lượng thuốc lẽ ra để uống trong ba ngày.
        Anh uống rượu tới mức ngồi không nổi, tôi gọi không thấy anh đáp.
        Bé vừa nuốt mất một miếng ghép nhựa tháo từ ô tô nhỏ.
        Cái khuy áo biến khỏi miệng cháu, giờ cháu chỉ vào cổ và ho.
        Đứa nhỏ lấy viên tròn từ hộp máy tính rồi nuốt, thấy đó là viên pin.
        Dung dịch trong cốc thực ra dùng để thông bồn rửa, chị vừa uống một ngụm.
        Đang tẩy sàn bằng dung dịch đậm đặc, cô bị bắn thẳng vào con ngươi.
        Anh vừa hít hơi từ thùng sơn ở góc kín, bây giờ lảo đảo và thở gấp.
        Ông đem nấm mọc hoang nấu ăn, giờ nôn nhiều và vã mồ hôi.
        Keo em ra khoi ao van chi thay moi tham va nguc nam im.
        Bỏng nước sôi một mảng to, lớp da ngoài lột hết ngay sau đó.
        Vừa ngã cầu thang đập đầu, bạn hỏi đi hỏi lại rồi lịm đi.
        Chị tôi đang bầu, máu tươi thấm cả váy và chị đứng không nổi.
        Môi em sơ sinh nhợt nhạt, người nóng bỏng và không chịu mút sữa.
        Sau cú va xe mạnh, tôi thấy bạn nằm im, gương mặt tím và hơi thở rất yếu.
        Cơn đau đầu ập đến dữ dội như bị đánh, ngay sau đó mắt mờ và không đứng vững.
        """.Split('\n');

    internal static readonly string[] TestRoutine = """
        Một chai si rô đã mở nắp cần ghi ngày bắt đầu ở đâu?
        Bản toa của tôi có hướng dẫn một liều buổi tối, muốn hỏi lại bộ phận nhà thuốc.
        Uong dung so vien thuoc bac si ke moi bua sang thi lay lai toa o dau?
        Tôi còn vài viên đến cuối đợt, phòng khám mở cửa để lấy thuốc ngày nào?
        Hộp thuốc ghi dùng trong 7 ngày, nhờ lễ tân hướng dẫn cách in đơn.
        Bao nhieu tien mot chai nuoc muoi sinh ly?
        Chỗ bán pin máy đo huyết áp có mở vào hôm nay?
        Tôi hỏi giá rượu dùng nấu ăn cho quán, không hỏi lịch khám.
        Lần trước dùng dầu gội bị ngứa nhẹ nhưng ngừng dùng là khỏi, xin lịch kiểm tra.
        Ngay mai co dang ky tiem phong mui nhac duoc khong?
        Trẻ đã tiêm đủ, nhà trường yêu cầu giấy xác nhận lịch chủng ngừa.
        Chiều thứ tư tôi muốn chuyển lịch tiêm sang cơ sở gần hơn.
        Em đang tìm bảng dịch vụ tiêm cho người lớn.
        Tiền khám trước tiêm có được công khai trên cổng thông tin không?
        Tôi thấy viên bi lăn sau chân ghế, bé vẫn ngồi ăn như thường.
        Hạt nhựa còn đủ trong hũ, cháu chưa bỏ thứ gì vào miệng.
        Con không ngậm pin, pin vẫn nằm nguyên trong thiết bị.
        Cháu thử ăn cơm nát một ít, không ho, vẫn chạy chơi.
        Anh trai tập chạy xong vẫn nói chuyện bình thường, muốn đặt lịch thể thao.
        Mẹ từng bị đau ngực, đã hoàn tất điều trị, cần xem lịch kiểm tra theo hẹn.
        Vết xước đã đóng vảy từ mấy hôm trước, nay chỉ cần đổi băng.
        Bé đã được khám sau lần nuốt dị vật năm trước, hôm nay sức khỏe tốt.
        Ông đang phục hồi và vẫn nói chuyện rõ, đến hẹn khám lại ngày nào?
        Đọc tài liệu về bỏng hóa chất cần hỏi thư viện nào?
        Chúng tôi cất dung dịch vệ sinh trong tủ khóa, chưa có ai chạm vào.
        Hôm trước nhà hàng đổi bình gas, cả nhà vẫn khỏe.
        Nong muon chet, cho ngoi doi co bat may lanh khong?
        Ngứa quá, cần tìm địa chỉ khám da cuối tuần.
        Bác sĩ có làm việc ngày nghỉ không và xem giờ ở đâu?
        Mua nam châm làm dụng cụ thí nghiệm môn vật lý ở đâu?
        Nhà có hạt điều rang sẵn, con vừa ăn trưa một phần nhỏ.
        Tập huấn xử lý tai nạn của trường bắt đầu lúc mấy giờ?
        """.Split('\n');
}
