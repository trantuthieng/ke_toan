// Load giao dịch để sửa
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.btn-edit').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var id = this.getAttribute('data-id');
            fetch('/Home/GetGiaoDich?id=' + id)
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    document.getElementById('edit-Id').value = data.id;
                    document.getElementById('edit-Ngay').value = data.ngay;
                    document.getElementById('edit-TenLai').value = data.tenLai || '';
                    document.getElementById('edit-TenKhach').value = data.tenKhach;
                    document.getElementById('edit-SoCon').value = data.soCon;
                    document.getElementById('edit-SoLuong').value = data.soLuong;
                    document.getElementById('edit-Gia').value = data.gia;
                    document.getElementById('edit-ThanhTien').value = data.thanhTien;
                    document.getElementById('edit-TienTraLai').value = data.tienTraLai;
                    document.getElementById('edit-GhiChu').value = data.ghiChu || '';
                });
        });
    });

    // Tự tính thành tiền khi nhập SL và Giá
    function setupAutoCalc(form) {
        var slInput = form.querySelector('input[name="SoLuong"]');
        var giaInput = form.querySelector('input[name="Gia"]');
        var ttInput = form.querySelector('input[name="ThanhTien"]');
        if (slInput && giaInput && ttInput) {
            function calc() {
                var sl = parseFloat(slInput.value) || 0;
                var gia = parseFloat(giaInput.value) || 0;
                if (sl > 0 && gia > 0) {
                    ttInput.value = Math.round(sl * gia);
                }
            }
            slInput.addEventListener('input', calc);
            giaInput.addEventListener('input', calc);
        }
    }

    // Apply auto-calc to both add and edit forms
    document.querySelectorAll('form').forEach(setupAutoCalc);

    // Lưu tab hiện tại vào URL hash
    var tabs = document.querySelectorAll('#mainTabs button[data-bs-toggle="tab"]');
    tabs.forEach(function (tab) {
        tab.addEventListener('shown.bs.tab', function (e) {
            window.location.hash = e.target.getAttribute('data-bs-target');
        });
    });

    // Restore tab từ hash
    var hash = window.location.hash;
    if (hash) {
        var tab = document.querySelector('#mainTabs button[data-bs-target="' + hash + '"]');
        if (tab) {
            var bsTab = new bootstrap.Tab(tab);
            bsTab.show();
        }
    }

    // === TAB 4: HOÀN THIỆN DỮ LIỆU ===

    // Khi chuyển sang tab hoàn thiện → load dữ liệu
    var btnHoanThien = document.getElementById('btn-tab-hoan-thien');
    if (btnHoanThien) {
        btnHoanThien.addEventListener('shown.bs.tab', function () {
            loadUploadedImages();
            loadIncompleteRecords();
        });
    }

    // Check URL param for tab
    var urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('tab') === 'hoan-thien') {
        var htTab = document.getElementById('btn-tab-hoan-thien');
        if (htTab) {
            var bsHtTab = new bootstrap.Tab(htTab);
            bsHtTab.show();
        }
    }

    // Refresh images button
    var btnRefresh = document.getElementById('btnRefreshImages');
    if (btnRefresh) {
        btnRefresh.addEventListener('click', loadUploadedImages);
    }

    // Save all buyer names
    var btnSave = document.getElementById('btnSaveKhachMua');
    if (btnSave) {
        btnSave.addEventListener('click', saveKhachMua);
    }

    function loadUploadedImages() {
        var gallery = document.getElementById('imageGallery');
        if (!gallery) return;

        fetch('/Home/GetUploadedImages')
            .then(function (r) { return r.json(); })
            .then(function (images) {
                if (images.length === 0) {
                    gallery.innerHTML = '<p class="text-muted text-center p-3">Chưa có ảnh nào. Hãy upload ảnh chụp sổ ghi tay.</p>';
                    return;
                }
                var html = '';
                images.forEach(function (src) {
                    html += '<div class="mb-2 position-relative">';
                    html += '<a href="' + src + '" target="_blank">';
                    html += '<img src="' + src + '" class="img-fluid rounded border" style="cursor:zoom-in" />';
                    html += '</a>';
                    html += '<button class="btn btn-sm btn-danger position-absolute top-0 end-0 m-1 btn-delete-img" data-file="' + src.split('/').pop() + '">';
                    html += '<i class="bi bi-x"></i></button>';
                    html += '</div>';
                });
                gallery.innerHTML = html;

                // Bind delete buttons
                gallery.querySelectorAll('.btn-delete-img').forEach(function (btn) {
                    btn.addEventListener('click', function (e) {
                        e.preventDefault();
                        if (!confirm('Xóa ảnh này?')) return;
                        var fileName = this.getAttribute('data-file');
                        fetch('/Home/XoaImage', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                            body: 'fileName=' + encodeURIComponent(fileName)
                        }).then(function () { loadUploadedImages(); });
                    });
                });
            });
    }

    function loadIncompleteRecords() {
        var container = document.getElementById('incompleteRecords');
        if (!container) return;

        var ngay = document.querySelector('input[name="ngay"]');
        var ngayVal = ngay ? ngay.value : '';

        fetch('/Home/GetIncompleteRecords?ngay=' + ngayVal)
            .then(function (r) { return r.json(); })
            .then(function (records) {
                if (records.length === 0) {
                    container.innerHTML = '<p class="text-muted text-center p-3">Tất cả giao dịch đã có tên khách mua! <i class="bi bi-check-circle text-success"></i></p>';
                    return;
                }

                // Group by TenLai
                var groups = {};
                records.forEach(function (r) {
                    var lai = r.tenLai || '(không rõ)';
                    if (!groups[lai]) groups[lai] = [];
                    groups[lai].push(r);
                });

                var html = '';
                for (var lai in groups) {
                    html += '<div class="mb-3">';
                    html += '<h6 class="fw-bold text-primary border-bottom pb-1"><i class="bi bi-truck"></i> Lái: ' + lai + ' (' + groups[lai].length + ' dòng)</h6>';
                    html += '<table class="table table-sm table-bordered" style="font-size:0.85rem">';
                    html += '<thead class="table-light"><tr><th style="width:30px">#</th><th style="width:50px">SC</th><th style="width:70px">SL</th><th style="width:50px">Giá</th><th style="width:80px">T.Tiền</th><th>Tên khách mua</th></tr></thead>';
                    html += '<tbody>';
                    var idx = 1;
                    groups[lai].forEach(function (r) {
                        html += '<tr>';
                        html += '<td>' + (idx++) + '</td>';
                        html += '<td class="text-end">' + r.soCon + '</td>';
                        html += '<td class="text-end">' + r.soLuong.toLocaleString() + '</td>';
                        html += '<td class="text-end">' + r.gia.toLocaleString() + '</td>';
                        html += '<td class="text-end fw-bold">' + r.thanhTien.toLocaleString() + '</td>';
                        html += '<td><input type="text" class="form-control form-control-sm khach-mua-input" data-id="' + r.id + '" placeholder="Nhập tên khách mua..." list="danhSachKhach" /></td>';
                        html += '</tr>';
                    });
                    html += '</tbody></table></div>';
                }

                container.innerHTML = html;
            });
    }

    function saveKhachMua() {
        var inputs = document.querySelectorAll('.khach-mua-input');
        var items = [];
        inputs.forEach(function (input) {
            var val = input.value.trim();
            if (val) {
                items.push({ id: parseInt(input.getAttribute('data-id')), tenKhach: val });
            }
        });

        if (items.length === 0) {
            alert('Chưa nhập tên khách mua nào!');
            return;
        }

        fetch('/Home/CapNhatTenKhach', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(items)
        })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) {
                    alert('Đã cập nhật ' + result.updated + ' giao dịch!');
                    loadIncompleteRecords();
                } else {
                    alert('Lỗi: ' + (result.message || 'Không thể cập nhật'));
                }
            })
            .catch(function (err) {
                alert('Lỗi kết nối: ' + err.message);
            });
    }
});
