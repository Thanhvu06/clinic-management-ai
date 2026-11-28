namespace ClinicManagement.Infrastructure.AI;

internal static class PatientEmergencyExamples
{
    internal static readonly string[] Emergency = """
        Mẹ đang nói thì gục xuống, chạm vào cũng không phản ứng.
        Ong toi do guc o san, hoi gi cung khong tra loi.
        Vừa ăn cơm xong anh tôi đờ người, lay vai mãi vẫn im.
        Tôi thấy bố nằm sõng soài và không mở mắt dù gọi rất to.
        Đứa nhỏ cứ lim dim, gọi tên mà nó chẳng nhận ra ai.
        Người nhà tôi bất động, tôi thử gọi nhưng họ không đáp.
        Dang dung xep hang thi mot nguoi nga quy va khong dap lai.
        Chị tôi tự nhiên không biết mình đang ở đâu, nói chuyện không còn mạch lạc.
        Sau cú va chạm cậu bé nhìn trân trân, không phản ứng khi gọi.
        Ông nhà tôi vừa mất ý thức và vẫn nằm im.
        Em toi khuỵu xuong, co ban dang lo no khong tinh.
        Cô ấy vừa đổ người ra sau và không trả lời khi tôi hỏi.
        Bố tôi đang cố hít không khí nhưng không nói nổi một câu.
        Toi nghe con tho rit va thay long nguc lom xuong.
        Chị tôi vật vã tìm hơi thở, người bắt đầu tím.
        Em bé thở ngáp rồi im bặt, ngực không nhúc nhích nữa.
        Ba dang tho gap den muc khong noi tron mot cau.
        Cổ họng tôi co chặt lại, mỗi lần hít vào rất chật vật.
        Người bên cạnh tôi thở yếu dần, môi chuyển màu xanh.
        Toi dang ngop, hut hoi mai van khong duoc.
        Con tôi gắng thở đến nỗi cánh mũi phập phồng và không bú nổi.
        Cứ hít vào là nghe tiếng rít to, giờ chị ấy không nói được.
        Người nhà tôi gần như không còn thở sau khi vớt lên bờ.
        Anh ấy gập người, cố hớp từng hơi một.
        Môi và mặt em bé đang tím dần sau cơn ho.
        Cháu đang ăn thì giật mình rồi ôm cổ, không phát ra tiếng.
        Con dang an thit thi ho du doi va khong hut hoi vao duoc.
        Một người ôm cổ ngay khi vừa nuốt thức ăn, mặt đỏ bừng rồi tím.
        Bé vừa nuốt miếng bánh, giờ nghe tiếng khò khè và rất khó lấy hơi.
        Mẹ sặc nước rồi ho nghẹn mãi, không nói thành tiếng.
        Vua an qua nho xong be dot nhien im bat va tim mat.
        Đồng nghiệp tôi bị mắc thức ăn ở cổ, cố nói nhưng chẳng ra tiếng.
        Bé ngậm đồ chơi, một mảnh biến mất và bé bắt đầu nghẹt thở.
        Anh ấy hít phải thứ gì, vừa ho dữ dội vừa ôm lấy họng.
        Toi thay chau nuot mot mieng vo chai, gio chau dang giu co.
        Em tôi đang lên cơn giật, hai tay chân rung không kiểm soát.
        Cha toi nga xuong, mat tro len va toan than giat lien tuc.
        Lần đầu con tôi cứng đờ, sau đó tay chân giật mạnh.
        Miệng chị tôi có bọt, người rung từng hồi từ nãy đến giờ.
        Anh ấy nằm dưới đất, chân tay co rút rồi giật từng đợt.
        Be nho dang sot va tu nhien giat manh ca nguoi.
        Cậu tôi vừa giật hết người xong vẫn chưa trả lời tôi.
        Con tôi co cứng người, môi tím và mắt trợn.
        Người nhà nằm bất động xen từng lúc giật cả hai chân.
        Môi bé thâm đi trong lúc cả tay lẫn chân rung bần bật.
        Tôi vừa nôn một bãi đỏ sẫm có cục máu.
        Ba toi oi ra chat mau nau sam nhu ba ca phe.
        Máu đỏ tươi trộn trong dịch nôn của mẹ ngay lúc này.
        Anh tôi đi tiêu, nước trong bồn cầu đỏ vì máu.
        Con toi vua dai tien ra mau va gio xanh xao.
        Phân của chị đen quánh như nhựa đường và chị đang choáng.
        Bố khạc lên cả một ngụm máu, thấy rất yếu.
        Cháu đi cầu toàn máu, bụng đau quặn và người tái.
        Em tôi nôn ra thứ lợn cợn đen như bã cà phê rồi hoa mắt.
        Tôi đi vệ sinh thấy toàn máu đỏ, giờ đứng không vững.
        Vết đứt ở tay cứ phun máu ra, khăn ướt hết rồi.
        Vet thuong chay mau uot dam vai du da an giu.
        Máu ở chân vẫn chảy thành dòng dù đã ép bằng khăn.
        Bạn vừa va vào kính, vật còn cắm ở đùi và máu chảy rất nhiều.
        Tay tôi bị cắt sâu, máu tuôn liên tục.
        Máu bên dưới băng vẫn thấm nhanh và nhỏ xuống sàn.
        Nguoi bi nga xe chay mau rat nhieu, da tro nen lanh.
        Mẹ mang thai đang ra nhiều máu, người lảo đảo.
        Cánh tay dập nặng, máu chảy mãi không ngừng.
        Tôi lấy mấy lớp gạc ép lên nhưng máu cứ phì ra ngoài.
        Bố ôm giữa ngực, nói bị ép như có tảng đá đè lên.
        Toi thay tuc nguc nhu bi de nghien va ra mo hoi lanh.
        Ngực tôi đau thắt dữ dội, đau lan qua vai trái.
        Mẹ bỗng ôm ngực, vã mồ hôi và hụt hơi.
        Tôi có cảm giác một cái đai siết chặt quanh lồng ngực.
        Co ay len con dau nguc kem buon non va lanh nguoi.
        Ông đột nhiên đau giữa xương ức, không đứng dậy được.
        Anh tôi nói tim như bị bóp nghẹt, mặt tái nhợt.
        Đang ngồi nghỉ thì ngực tôi bỗng đau như bị đâm.
        Cơn đè nặng từ ngực lan ra hàm, tôi xây xẩm và vã mồ hôi.
        Ba tôi đang ăn thì một bên mặt trễ xuống, nói không rõ.
        Me toi mat can doi mat, tay phai dot nhien khong nang noi.
        Bà vừa nói bình thường, giờ miệng lệch và cầm đồ rơi liên tục.
        Tôi bỗng nhiên không điều khiển được nửa người bên trái.
        Anh toi vua dang di thi mot chan khong con suc va noi liu luoi.
        Một lúc trước còn ổn, giờ ông không nâng được tay bên phải.
        Cô ấy đột ngột nói líu lưỡi và mất thăng bằng.
        Một bên môi không nhấc lên được khi tôi bảo bố cười.
        Ông đang ăn thì đột nhiên đũa rơi, tay bên trái thõng xuống.
        Chị tôi đột nhiên không hiểu người khác nói và nói toàn từ rời rạc.
        Vừa tiêm xong cổ họng tôi thắt lại và lưỡi sưng lên.
        Moi chau phong to va hoi tho rit sau khi an.
        Bạn tôi nổi mề đay khắp người, môi sưng và đang vật lộn để thở.
        Sau khi bị ong chích, người tôi nóng bừng, cổ sưng và hụt hơi.
        Em tôi dị ứng, giờ giọng khàn nhanh và mặt sưng phồng.
        Sau bua an mat me sung vu len, noi thi hut hoi.
        Vừa uống viên mới, lưỡi tôi dày lên nhanh và cảm giác bị bít cổ.
        Ngay sau truyền thuốc ông tôi choáng rồi thở rất khó.
        Nốt mẩn lan nhanh, môi em sưng, hít vào có tiếng rít.
        Chị bị đốt rồi lả đi, phù quanh miệng và thở yếu.
        Trẻ trong nhà lỡ uống một phần lớn chai thuốc nước của bà.
        Be vua doc nguoc ca chai thuoc cam vao mieng.
        Mẹ tôi dùng liền mấy liều thuốc hôm nay vì quên đã uống.
        Chị lấy nhầm lọ rồi uống một ngụm nước tẩy, giờ bỏng rát họng.
        Cháu uống cạn thuốc hạ sốt trong lọ còn đầy.
        Toi pha nham dung dich rua nha vao coc va vua uong mot ngum.
        Ông uống lượng thuốc gấp ba toa cùng lúc.
        Con tôi nuốt hàng loạt viên lấy từ hộp thuốc người lớn.
        Hộp thuốc hôm qua còn nguyên, giờ em báo đã nuốt sạch số viên.
        Be vua uong thuoc cua bo, khong ro het bao nhieu vien.
        Bố tôi uống liên tiếp nhiều ly rượu mạnh rồi nôn và không còn tỉnh.
        Anh tôi rót rượu đầy ly uống hết nhiều lần, giờ lay không dậy.
        Cậu tôi uống gần hết lọ dung dịch sát khuẩn vì tưởng đồ uống.
        Toi lo uong lieu cao gap doi don bac si vua ke.
        Đứa cháu tưởng thuốc là kẹo và ăn liên tục nhiều viên.
        Bạn vừa dốc một lượng lớn si rô từ lọ vào miệng.
        Bé nhặt hạt cườm trên bàn rồi nuốt vào bụng.
        Chau da nuot cuc pin nho trong den nhay.
        Đang chơi bé ngậm tiền kim loại rồi lỡ đưa xuống họng.
        Trẻ nhỏ lấy hai viên nam châm ra khỏi hộp rồi ăn mất.
        Con tôi có viên bi mắc trong cổ sau lúc chơi.
        Be hit mot chi tiet nhua roi bat dau ho khong dut.
        Bé mút cúc áo, giờ cúc đã vào họng và không lấy ra được.
        Cháu đang chơi và vừa nuốt một mảnh nhựa gãy.
        Tôi thấy đứa nhỏ vừa hít hạt nhỏ vào đường thở.
        Con nuốt một mẩu đồ chơi thì òa khóc và giữ lấy cổ.
        Người trong phòng để máy nổ kín cửa, hai người đều choáng và lả đi.
        Toi hit khoi dam chay trong nha va gio tho rat met.
        Từ lúc hít hơi lạ trong kho, mắt tôi cay và ngực như bị ép.
        Lúc đổ hóa chất tẩy vào bồn, dung dịch văng thẳng vào mắt bạn tôi.
        Bé cầm chất tẩy và đổ lên mặt, mắt nhắm chặt vì đau.
        Ba toi lo nuot chat rua ong, mieng co cam giac bong.
        Hơi hóa chất thoát ra trong phòng, chúng tôi ho liên hồi và choáng.
        Ông tôi ăn loại nấm nhặt ngoài vườn rồi nôn không ngừng.
        Người này vừa uống nhầm chất đựng trong can vệ sinh.
        Dung môi bắn vào mắt làm cô ấy không mở mắt được.
        Đứa bé mới sinh nóng ran, bỏ bú và nằm mềm oặt.
        Sau cú ngã đập đầu anh ấy nôn rồi dần dần không nhận ra tôi.
        Thoat khoi lua, anh toi noi khan va tho kho hon tung phut.
        Tôi vừa bị điện giật, giờ đau tức ngực và hoa mắt.
        Người vừa rơi từ trên cao nằm không cử động, hỏi không trả lời.
        Cậu ấy đang dưới nước thì chìm, kéo lên rồi vẫn không thấy thở.
        Em toi bong dien rong, lop da bong ra va nguoi dang lanh di.
        Trán con tôi đập vào gạch, sau đó cháu lờ đờ và nôn nhiều.
        Mẹ chợt đau đầu kinh khủng, sau đó nói khó và bước không nổi.
        Bé ngã xuống hồ, kéo lên chỉ thở ngáp, mặt tím.
        Bà tôi đang đi thì tối sầm mặt rồi gục xuống rất lâu.
        Anh ấy bị xe tông, co quắp nằm trên đường và máu ra nhiều.
        Ông bị đập đầu và gọi hỏi mấy lần không trả lời.
        Em bé đã thôi khóc sau khi sặc, cả người mềm rũ và môi lam.
        Khi tỉnh khi mê, anh tôi chỉ lẩm bẩm rồi lịm dần.
        Sau bữa ăn, chị ngã gục vì họng sưng lên rất nhanh.
        Ở nơi làm việc có người bỗng đổ vật xuống, da xám và hơi thở yếu ớt.
        Người thân vừa rời khỏi bếp đầy khói, nôn rồi không giữ được tỉnh táo.
        Một mảnh kim loại làm thủng chân, cứ mỗi nhịp tim máu lại vọt ra.
        Tôi thấy một vệt máu đỏ trào lên mỗi lần ông nôn.
        Cậu bé đứng dựa tường, níu cổ và thở hụt hẫng từng tiếng.
        """.Split('\n');

    internal static readonly string[] Routine = """
        Liều uống si rô trị ho trên nhãn là bao nhiêu?
        Thuoc cam nguoi lon co nhung dang bao che nao?
        Tôi uống một viên theo đơn bác sĩ sau bữa sáng.
        Thuoc cua toi ghi hai vien moi ngay, co can chia hai lan?
        Nhờ phòng khám giải thích dòng hướng dẫn trên hộp thuốc.
        Dược sĩ dặn một viên khi ăn tối, tôi muốn biết giờ nhà thuốc.
        Uống hết liệu trình tháng trước rồi, nay tôi muốn tái khám.
        Tôi đã nhờ dược sĩ kiểm tra lại, viên này đúng thuốc trong toa.
        Hướng dẫn bảo quản si rô sau khi mở nắp ở đâu?
        Nhà thuốc có kiểm tra tương tác giữa các thuốc trong đơn không?
        Thuoc si ro con nua chai bao quan the nao?
        Một vỉ có bao nhiêu viên và giá một vỉ là bao nhiêu?
        Tôi đã uống một liều vitamin buổi sáng theo đơn.
        Bao bi thuoc ghi uong sau bua an nghia la gi?
        Chỗ nào xem được kế hoạch uống thuốc bác sĩ đã kê?
        Toa mới chỉ đổi giờ uống, tôi muốn hỏi lại lễ tân về lịch hẹn.
        Tôi không uống thuốc của người khác, muốn mang thuốc cũ đến để hỏi.
        Mai tôi muốn nhờ dược sĩ kiểm tra hạn dùng của si rô.
        Tôi quên mang đơn thuốc, có mở được đơn trong tài khoản không?
        Con tôi đã uống đúng liều được dặn trong buổi khám.
        Lịch tiêm nhắc cho bé được hiển thị ở mục nào?
        Mai em có thể đặt lịch tiêm phòng cúm buổi sáng không?
        Lich vac xin o co so nay co vao chieu chu nhat khong?
        Tôi muốn dời hẹn tiêm vắc xin sang tuần sau.
        Lần trước tiêm xong tôi ổn, nay đến để lấy giấy xác nhận.
        Đăng ký tiêm chủng cho trẻ có cần sổ không?
        Tiêm một liều vắc xin hết bao nhiêu tiền?
        Gio lam viec cua diem tiem phong la may gio?
        Tôi đã tiêm đủ theo lịch, cần in lại giấy tiêm.
        Cuối tuần phòng khám có tiếp nhận người đến hỏi lịch tiêm không?
        Có thể xem bảng giá thuốc ho trong tài khoản không?
        Cho xin gia cua siro va vitamin cho nguoi lon.
        Thuốc hạ sốt còn hàng ở quầy không?
        Pin đồng hồ mua ở cửa hàng nào?
        Pin sac du phong 10 nghin co gia bao nhieu?
        Giá một hộp thuốc bao gồm phí tư vấn không?
        Chai rượu dùng làm quà có giá thế nào?
        Tôi đang so sánh giá sản phẩm vệ sinh nhà bếp.
        Chỗ nào xem giá khám và chi phí thuốc?
        Chất tẩy để lau nhà hạn dùng mấy năm?
        Con vừa nghịch pin nhưng tôi lấy lại trước khi nó cho vào miệng.
        Cháu nhặt nút áo, tôi cất đi rồi, cháu vẫn đang chơi bình thường.
        Hôm qua bé ngậm kẹo, hiện ăn uống và thở bình thường.
        Bé làm rơi tiền xu, tôi đã tìm thấy đồng xu dưới sàn.
        Em tôi uống nước lọc, viên pin nằm cạnh nhưng chưa hề chạm vào.
        Cháu không nuốt đồ chơi, tôi vừa tìm đủ các mảnh.
        Me toi bi sac nuoc hom truoc, bac si da kiem tra va cho ve.
        Lần trước tôi thấy khó thở, đã được điều trị hết, hỏi lịch tái khám.
        Vết cắt tuần trước đã lành, hôm nay chỉ muốn tháo chỉ theo hẹn.
        Bà đã xuất viện từ lâu và ổn định, nay muốn xem lại kết quả.
        Ông có tiền sử đột quỵ, ăn nói bình thường và cần đặt lịch phục hồi.
        Con toi khong sot, dang hen kiem tra dinh ky.
        Mẹ không ngất hay hoa mắt, chỉ cần làm giấy khám định kỳ.
        Xuất viện hôm qua và bác sĩ dặn lấy đơn tái khám vào tháng sau.
        Bé đi ngoài bình thường, tôi định hỏi giờ làm việc của bác sĩ.
        Hôm nay tôi thấy hơi ngứa tay, không có gì bất thường khác.
        Bác sĩ dặn theo dõi, vết thương khô và đã hết chảy máu.
        Tôi bị ho nhẹ, vẫn đọc truyện và nói chuyện được bình thường.
        Sau bữa ăn có chút đầy bụng, tôi muốn hỏi lịch khoa tiêu hóa.
        Nhà tôi muốn tránh để trẻ với tới các đồ vật nhỏ.
        Tôi muốn tìm hiểu cách cất thuốc tẩy an toàn.
        Khi đi tham quan phòng cháy, có thể xin tài liệu hướng dẫn không?
        Thứ nên mang khi đi kiểm tra sức khỏe tổng quát là gì?
        Lịch tập huấn sơ cứu ở trường có mở đăng ký chưa?
        Có tài liệu thông tin công khai về phòng ngừa ngộ độc không?
        Dầu hỏa dùng trong đèn được đóng gói thế nào?
        Khí gas bán đổi vỏ bình ở đâu?
        Tôi xem một đoạn phim về cấp cứu và muốn học sơ cứu.
        Hoi suc la gi trong mot bai giang y khoa?
        Có biển chỉ dẫn kho lưu hóa chất ở trong trường không?
        Buồn ngủ muốn chết, sáng mai phòng khám mở cửa lúc mấy giờ?
        Lanh chet mat, phong cho co kinh chan gio khong?
        Ngoài trời nóng quá, chỗ đợi khám có quạt không?
        Da ngứa kinh khủng, muốn đăng ký khám da liễu.
        Mắc cười muốn chết, video này xem ở đâu?
        Đói chết đi được, có quán ăn nào gần phòng khám?
        Đau lưng nhiều khi ngồi lâu, khoa nào tiếp nhận?
        Sanh cho co phong ve sinh o dau?
        Cổ họng hơi khô sau khi nói cả buổi, tôi vẫn thở bình thường.
        Chỗ nào bán nước trong khu khám?
        Cô tôi hỏi giờ đi khám ngày thứ năm.
        Trẻ nhà tôi có lịch khám với bác sĩ buổi chiều.
        Xin xem địa chỉ cơ sở gần nhà tôi.
        Phòng khám có điều hòa ở khu chờ không?
        Ngày nghỉ có mở quầy lễ tân không?
        Bị mụn lâu ngày nên hỏi lịch da liễu.
        Hom nay kinh mat cua toi bi hong, co lich kham mat khong?
        Tôi muốn xem lại giờ hẹn khám trong tài khoản.
        Vào chủ nhật bác sĩ có lịch sáng hay chiều?
        Có đăng ký kiểm tra huyết áp định kỳ cho người già không?
        Rát da ở tay khi trời khô, nên khám chuyên khoa nào?
        Tôi bị ngứa mắt khi đọc sách, còn lịch nhãn khoa không?
        Nha toi co the doi lich tu sang sang buoi chieu khong?
        Ngày mai xét nghiệm có phải chuẩn bị giấy tờ gì?
        Tôi đã nhận thông báo lịch khám, bây giờ cần làm gì?
        Hỏi bác sĩ về tình trạng dị ứng nhẹ đã hết từ tháng trước.
        Bé chỉ ho nhẹ sau khi chạy nhảy, nghỉ một lát đã khỏi.
        Cậu tôi đã không còn choáng, vừa được bác sĩ khám và cho về.
        Ba tuan truoc bi dau dau, bay gio muon kiem tra theo hen.
        Mẹ cần hỏi cách lấy kết quả ở cơ sở đã khám.
        Tôi xem tin tức về vụ cháy, gia đình tôi không gặp sự cố.
        Không ai bị thương, tôi chỉ muốn hỏi giờ mở cửa lúc sáng sớm.
        Bé ăn một ít đậu đã nấu mềm trong bữa trưa.
        Chị mua viên bi làm đồ trang trí, để xa tầm tay trẻ.
        """.Split('\n');
}
