export interface SpecialtyContent {
    name: string;
    intro: string;
    conditions: string[];
    whenToVisit: string[];
    services: string[];
    prepare: string[];
}

export const specialtyContent: Record<string, SpecialtyContent> = {
    SP01: {
        name: 'Nội tổng quát',
        intro: 'Nội tổng quát tiếp nhận người lớn có vấn đề sức khỏe thường gặp hoặc triệu chứng chưa rõ thuộc chuyên khoa nào. Bác sĩ đánh giá tình trạng chung, theo dõi bệnh mạn tính và tư vấn khám chuyên khoa khi cần.',
        conditions: ['Ho kéo dài', 'Sốt tái diễn', 'Mệt mỏi', 'Tăng huyết áp', 'Rối loạn mỡ máu', 'Đau bụng', 'Thiếu máu'],
        whenToVisit: ['Mệt mỏi kéo dài ảnh hưởng sinh hoạt.', 'Ho hoặc sốt không cải thiện, hay tái phát.', 'Cân nặng thay đổi không rõ nguyên nhân.', 'Cần theo dõi bệnh mạn tính hoặc xem lại thuốc đang dùng.'],
        services: ['Khám nội khoa tổng quát', 'Đo huyết áp và đánh giá sinh hiệu', 'Xét nghiệm máu cơ bản khi có chỉ định', 'Tư vấn kiểm soát yếu tố nguy cơ', 'Hướng dẫn theo dõi và tái khám'],
        prepare: ['Mang kết quả khám và xét nghiệm gần đây.', 'Ghi lại triệu chứng, thời điểm bắt đầu và thuốc đang dùng.', 'Thông báo tiền sử bệnh và dị ứng thuốc.', 'Chỉ nhịn ăn khi đã được nhân viên y tế hướng dẫn.'],
    },
    SP02: {
        name: 'Nhi khoa',
        intro: 'Nhi khoa chăm sóc sức khỏe trẻ em, từ những bệnh thường gặp đến các vấn đề về dinh dưỡng và phát triển. Bác sĩ trao đổi cùng gia đình để hiểu biểu hiện của trẻ, thăm khám và hướng dẫn cách chăm sóc phù hợp với độ tuổi.',
        conditions: ['Sốt ở trẻ', 'Ho, sổ mũi', 'Viêm họng', 'Tiêu chảy', 'Táo bón', 'Biếng ăn', 'Chậm tăng cân', 'Phát ban'],
        whenToVisit: ['Trẻ ho, sốt hoặc mệt kéo dài, cần được bác sĩ đánh giá.', 'Trẻ ăn uống kém hoặc tăng cân không như trước.', 'Tiêu chảy, táo bón hay đau bụng tái diễn.', 'Gia đình lo lắng về giấc ngủ, dinh dưỡng hoặc sự phát triển của trẻ.', 'Nếu trẻ khó thở, tím tái, co giật hoặc khó đánh thức, cần đưa trẻ đi cấp cứu ngay.'],
        services: ['Khám và đánh giá sức khỏe trẻ em', 'Theo dõi cân nặng, chiều cao', 'Tư vấn dinh dưỡng theo độ tuổi', 'Đánh giá sự phát triển', 'Xét nghiệm cơ bản khi có chỉ định'],
        prepare: ['Mang sổ khám bệnh, sổ tiêm chủng và kết quả cũ của trẻ.', 'Ghi lại diễn biến sốt, ăn uống và số lần đi ngoài nếu có.', 'Mang theo danh sách thuốc và liều trẻ đã dùng.', 'Cho trẻ mặc quần áo thoải mái; không tự cho trẻ nhịn ăn trước khám.'],
    },
    SP03: {
        name: 'Sản phụ khoa',
        intro: 'Sản phụ khoa tiếp nhận các vấn đề sức khỏe sinh sản, kinh nguyệt và theo dõi thai kỳ. Việc thăm khám giúp người phụ nữ hiểu tình trạng của mình và trao đổi về kế hoạch chăm sóc phù hợp ở từng giai đoạn.',
        conditions: ['Kinh nguyệt không đều', 'Đau bụng kinh', 'Khí hư bất thường', 'Ngứa vùng kín', 'Viêm âm đạo', 'Khó chịu thời kỳ mãn kinh', 'Theo dõi thai kỳ'],
        whenToVisit: ['Chu kỳ kinh thay đổi hoặc ra máu bất thường.', 'Đau vùng bụng dưới tái diễn.', 'Khí hư thay đổi, ngứa hoặc khó chịu vùng kín.', 'Cần khám thai hoặc tư vấn trước khi mang thai.', 'Đau bụng dữ dội hoặc chảy máu nhiều, đặc biệt khi mang thai, cần được cấp cứu.'],
        services: ['Khám phụ khoa', 'Khám và tư vấn thai kỳ', 'Siêu âm khi có chỉ định', 'Tư vấn sàng lọc cổ tử cung', 'Tư vấn sức khỏe sinh sản'],
        prepare: ['Ghi ngày đầu kỳ kinh gần nhất và các triệu chứng.', 'Mang hồ sơ thai kỳ, kết quả khám và xét nghiệm trước đó.', 'Thông báo thuốc đang dùng và khả năng mang thai.', 'Trao đổi trước với cơ sở khám về chuẩn bị cho từng loại xét nghiệm.'],
    },
    SP04: {
        name: 'Da liễu',
        intro: 'Da liễu thăm khám các vấn đề về da, tóc và móng ở nhiều độ tuổi. Bác sĩ đánh giá tổn thương, tìm yếu tố liên quan và hướng dẫn chăm sóc để hạn chế kích ứng hoặc tái phát.',
        conditions: ['Mụn trứng cá', 'Viêm da cơ địa', 'Viêm da tiếp xúc', 'Mề đay', 'Nấm da', 'Rụng tóc', 'Bệnh ở móng'],
        whenToVisit: ['Da ngứa, đỏ hoặc nổi mẩn kéo dài.', 'Mụn ảnh hưởng sinh hoạt hoặc để lại tổn thương.', 'Tóc rụng nhiều, móng đổi màu hay biến dạng.', 'Nốt trên da thay đổi kích thước, màu sắc hoặc chảy máu.'],
        services: ['Khám da, tóc và móng', 'Đánh giá tổn thương da', 'Soi và xét nghiệm nấm khi có chỉ định', 'Tư vấn chăm sóc da', 'Tư vấn sử dụng thuốc bôi'],
        prepare: ['Mang danh sách thuốc bôi, mỹ phẩm và sản phẩm đang dùng.', 'Chụp lại tổn thương nếu biểu hiện thay đổi theo thời điểm.', 'Hạn chế trang điểm che phủ vùng cần khám.', 'Không tự ngừng thuốc đang được kê; trao đổi với bác sĩ trước.'],
    },
    SP05: {
        name: 'Tai mũi họng',
        intro: 'Tai mũi họng thăm khám các vấn đề nghe, thở và nuốt liên quan đến tai, mũi, họng. Người lớn và trẻ em có triệu chứng kéo dài hoặc tái phát có thể được đánh giá để tìm hướng xử trí phù hợp.',
        conditions: ['Viêm họng', 'Viêm mũi dị ứng', 'Viêm xoang', 'Đau tai', 'Ù tai', 'Khàn tiếng', 'Nghẹt mũi'],
        whenToVisit: ['Đau họng, nghẹt mũi tái diễn hoặc kéo dài.', 'Ù tai, đau tai hoặc nghe kém.', 'Khàn tiếng không cải thiện.', 'Nuốt vướng hoặc có cảm giác khó chịu trong họng thường xuyên.'],
        services: ['Khám tai mũi họng', 'Nội soi tai mũi họng khi có chỉ định', 'Đánh giá thính lực khi cần', 'Tư vấn chăm sóc mũi họng', 'Hướng dẫn theo dõi triệu chứng'],
        prepare: ['Mang kết quả nội soi và đo thính lực cũ nếu có.', 'Ghi các thuốc xịt, nhỏ tai hoặc thuốc uống đang dùng.', 'Thông báo tiền sử dị ứng và bệnh nền.', 'Không tự ngoáy tai hoặc đưa vật vào mũi trước khám.'],
    },
    SP06: {
        name: 'Tim mạch',
        intro: 'Tim mạch đánh giá hoạt động của tim, huyết áp và các yếu tố nguy cơ tim mạch ở người lớn. Người có bệnh tim mạch cần theo dõi định kỳ để bác sĩ xem xét triệu chứng và điều chỉnh kế hoạch chăm sóc khi phù hợp.',
        conditions: ['Tăng huyết áp', 'Hồi hộp, đánh trống ngực', 'Rối loạn nhịp tim', 'Rối loạn mỡ máu', 'Bệnh mạch vành', 'Suy tim', 'Bệnh van tim'],
        whenToVisit: ['Huyết áp nhiều lần cao hơn mức bác sĩ hướng dẫn.', 'Hồi hộp hoặc nhịp tim bất thường tái diễn.', 'Khó thở khi gắng sức hoặc phù chân mới xuất hiện.', 'Cần theo dõi bệnh tim và các thuốc đang dùng.', 'Đau ngực đột ngột không hết, kèm khó thở, vã mồ hôi hoặc choáng cần cấp cứu ngay.'],
        services: ['Khám tim mạch', 'Đo huyết áp', 'Đo điện tâm đồ khi có chỉ định', 'Siêu âm tim khi có chỉ định', 'Xét nghiệm mỡ máu và tư vấn nguy cơ'],
        prepare: ['Mang sổ đo huyết áp, nhịp tim tại nhà nếu có.', 'Mang đơn thuốc và hồ sơ tim mạch trước đó.', 'Ghi lại thời điểm xuất hiện hồi hộp hoặc khó thở.', 'Không tự ngừng thuốc tim mạch trước khám.'],
    },
    SP07: {
        name: 'Cơ xương khớp',
        intro: 'Cơ xương khớp thăm khám tình trạng đau, sưng và hạn chế vận động của cơ, xương và khớp. Bác sĩ đánh giá ảnh hưởng đến sinh hoạt, tiền sử chấn thương và các bệnh liên quan để tư vấn chăm sóc.',
        conditions: ['Đau lưng', 'Đau vai gáy', 'Thoái hóa khớp', 'Viêm khớp', 'Gút', 'Loãng xương', 'Đau gân cơ'],
        whenToVisit: ['Đau khớp kéo dài hoặc ảnh hưởng đi lại.', 'Khớp sưng, cứng khớp khi thức dậy.', 'Đau lưng, cổ hoặc vai tái diễn.', 'Cần đánh giá nguy cơ loãng xương hoặc theo dõi bệnh khớp.'],
        services: ['Khám cơ xương khớp', 'Đánh giá tầm vận động', 'Xét nghiệm viêm khi có chỉ định', 'Chụp X-quang khi có chỉ định', 'Tư vấn vận động và bảo vệ khớp'],
        prepare: ['Mang phim chụp và kết quả xét nghiệm cũ.', 'Ghi vị trí đau, thời gian đau và hoạt động làm đau tăng.', 'Mặc quần áo dễ bộc lộ vùng cần khám.', 'Thông báo thuốc giảm đau, thực phẩm bổ sung đang dùng.'],
    },
    SP08: {
        name: 'Thần kinh',
        intro: 'Thần kinh tiếp nhận các vấn đề liên quan đến não, dây thần kinh và vận động. Bác sĩ tìm hiểu diễn biến triệu chứng và khám thần kinh để xác định có cần làm thêm xét nghiệm hay chuyển tuyến hay không.',
        conditions: ['Đau đầu tái diễn', 'Đau nửa đầu', 'Chóng mặt', 'Tê bì tay chân', 'Run tay', 'Đau dây thần kinh', 'Rối loạn giấc ngủ'],
        whenToVisit: ['Đau đầu hay chóng mặt tái diễn.', 'Tê bì kéo dài hoặc giảm cảm giác.', 'Run tay, thay đổi vận động ảnh hưởng sinh hoạt.', 'Giấc ngủ hoặc trí nhớ thay đổi đáng lo ngại.', 'Méo miệng, yếu liệt, nói khó đột ngột hoặc đau đầu dữ dội bất thường cần cấp cứu ngay.'],
        services: ['Khám thần kinh', 'Đánh giá cảm giác và vận động', 'Đánh giá trí nhớ khi cần', 'Xét nghiệm tìm nguyên nhân khi có chỉ định', 'Tư vấn theo dõi và chuyển khám phù hợp'],
        prepare: ['Ghi diễn biến và thời gian mỗi đợt triệu chứng.', 'Mang kết quả khám, phim chụp và đơn thuốc cũ.', 'Có thể đi cùng người thân nắm rõ biểu hiện của bạn.', 'Thông báo tất cả thuốc đang dùng và bệnh nền.'],
    },
    SP09: {
        name: 'Nội tiết',
        intro: 'Nội tiết thăm khám các bệnh liên quan đến hormone và chuyển hóa như đái tháo đường, bệnh tuyến giáp. Bác sĩ theo dõi triệu chứng, kết quả xét nghiệm và thói quen sinh hoạt để tư vấn quản lý bệnh lâu dài.',
        conditions: ['Đái tháo đường', 'Tiền đái tháo đường', 'Cường giáp', 'Suy giáp', 'Bướu giáp', 'Rối loạn mỡ máu', 'Thừa cân, béo phì'],
        whenToVisit: ['Khát nhiều, tiểu nhiều hoặc cân nặng thay đổi không rõ nguyên nhân.', 'Run tay, hồi hộp hoặc dễ nóng bất thường.', 'Mệt kéo dài, dễ lạnh hoặc thay đổi ở vùng cổ.', 'Cần theo dõi đường huyết, tuyến giáp và thuốc điều trị.'],
        services: ['Khám nội tiết', 'Đánh giá đường huyết khi có chỉ định', 'Xét nghiệm chức năng tuyến giáp khi cần', 'Tư vấn dinh dưỡng và vận động', 'Hướng dẫn theo dõi bệnh tại nhà'],
        prepare: ['Mang sổ đường huyết và kết quả xét nghiệm cũ.', 'Ghi thuốc, liều dùng và thời điểm dùng gần nhất.', 'Hỏi trước về việc nhịn ăn nếu có lịch xét nghiệm.', 'Không tự bỏ liều hoặc đổi liều thuốc trước khám.'],
    },
    SP10: {
        name: 'Nhãn khoa',
        intro: 'Nhãn khoa chăm sóc thị lực và thăm khám các bệnh ở mắt. Bác sĩ đánh giá triệu chứng, khả năng nhìn và những yếu tố ảnh hưởng đến mắt để hướng dẫn điều trị hoặc theo dõi phù hợp.',
        conditions: ['Cận thị', 'Viễn thị', 'Loạn thị', 'Khô mắt', 'Viêm kết mạc', 'Đục thủy tinh thể', 'Tăng nhãn áp'],
        whenToVisit: ['Nhìn mờ tăng dần hoặc phải nheo mắt để nhìn rõ.', 'Mắt khô, cộm hay đỏ tái diễn.', 'Cần kiểm tra thị lực hoặc theo dõi bệnh mắt.', 'Đột ngột mất thị lực, đau mắt dữ dội hoặc chấn thương mắt cần được khám cấp cứu.'],
        services: ['Khám mắt và kiểm tra thị lực', 'Đánh giá tật khúc xạ', 'Đo nhãn áp khi có chỉ định', 'Khám đáy mắt khi cần', 'Tư vấn chăm sóc và bảo vệ mắt'],
        prepare: ['Mang kính đang dùng và đơn kính cũ.', 'Mang thuốc nhỏ mắt cùng hồ sơ khám trước đó.', 'Thông báo bệnh nền, đặc biệt đái tháo đường.', 'Nên có người đi cùng nếu được hẹn nhỏ thuốc giãn đồng tử.'],
    },
    SP11: {
        name: 'Tiêu hóa',
        intro: 'Tiêu hóa thăm khám các vấn đề ở dạ dày, ruột, gan và đường mật. Bác sĩ trao đổi về triệu chứng, bữa ăn và thói quen đi ngoài để lựa chọn hướng đánh giá phù hợp với từng người.',
        conditions: ['Trào ngược dạ dày', 'Viêm dạ dày', 'Đầy bụng', 'Táo bón', 'Tiêu chảy kéo dài', 'Hội chứng ruột kích thích', 'Bệnh gan mạn tính'],
        whenToVisit: ['Đau bụng, ợ nóng hoặc đầy bụng tái diễn.', 'Thay đổi thói quen đi ngoài kéo dài.', 'Ăn kém, sụt cân hoặc vàng da cần được đánh giá.', 'Nôn ra máu, đi ngoài phân đen hoặc đau bụng dữ dội cần đến cấp cứu ngay.'],
        services: ['Khám tiêu hóa', 'Xét nghiệm máu hoặc phân khi có chỉ định', 'Siêu âm bụng khi có chỉ định', 'Tư vấn nội soi khi cần', 'Tư vấn dinh dưỡng và theo dõi triệu chứng'],
        prepare: ['Mang kết quả nội soi, siêu âm và xét nghiệm cũ.', 'Ghi triệu chứng liên quan đến bữa ăn và đi ngoài.', 'Thông báo thuốc chống đông, thuốc dạ dày đang dùng.', 'Chỉ nhịn ăn hoặc chuẩn bị nội soi khi đã nhận hướng dẫn riêng.'],
    },
};
