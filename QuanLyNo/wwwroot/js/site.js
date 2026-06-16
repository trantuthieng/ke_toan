// Load giao dịch để sửa
document.addEventListener('DOMContentLoaded', function () {
    setupSortableTables(document);
    setupExcelNavigation(document);
    setupDebtPaidFullToggles(document);
    hydrateDebtImageQuantities();
    document.addEventListener('click', handleSortableHeaderClick);
    document.addEventListener('keydown', handleSortableHeaderKeydown);

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
            loadImageImportReview();
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

    var btnRefreshImageReview = document.getElementById('btnRefreshImageReview');
    if (btnRefreshImageReview) {
        btnRefreshImageReview.addEventListener('click', loadImageImportReview);
    }

    document.querySelectorAll('#imageLoaiImport, #imageNguonBanHang').forEach(function (select) {
        select.addEventListener('change', function () {
            loadImageImportReview();
            hydrateDebtImageQuantities();
        });
    });

    var btnApplyImageReview = document.getElementById('btnApplyImageReview');
    if (btnApplyImageReview) {
        btnApplyImageReview.addEventListener('click', applyImageImportReview);
    }

    // Save all buyer names
    var btnSave = document.getElementById('btnSaveKhachMua');
    if (btnSave) {
        btnSave.addEventListener('click', saveKhachMua);
    }

    var btnSaveTraNoBang = document.getElementById('btnSaveTraNoBang');
    if (btnSaveTraNoBang) {
        btnSaveTraNoBang.addEventListener('click', saveBangTraNo);
    }

    var imageFiles = document.getElementById('imageFiles');
    if (imageFiles) {
        imageFiles.addEventListener('change', function () {
            inferImageImportFromFiles(imageFiles.files);
        });
    }

    var imageImportForm = document.getElementById('imageImportForm');
    if (imageImportForm) {
        imageImportForm.addEventListener('submit', function (e) {
            e.preventDefault();
            setImageImportBusy(true);
            var formData = new FormData(imageImportForm);
            fetch('/Home/ImportImageBatch', {
                method: 'POST',
                body: formData
            })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                setImageImportBusy(false);
                if (data && data.success) {
                    imageImportForm.reset();
                    loadImageImportReview();
                    loadIncompleteRecords();
                } else {
                    alert('Lỗi upload: ' + (data && data.error ? data.error : 'Không rõ nguyên nhân.'));
                }
            })
            .catch(function (err) {
                setImageImportBusy(false);
                alert('Lỗi kết nối: ' + err);
            });
        });
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
                        html += '<td><input type="text" class="form-control form-control-sm khach-mua-input" data-id="' + r.id + '" placeholder="Nhập tên khách mua..." list="danhSachTen" /></td>';
                        html += '</tr>';
                    });
                    html += '</tbody></table></div>';
                }

                container.innerHTML = html;
                setupSortableTables(container);
                setupExcelNavigation(container);
            });
    }

    function loadImageImportReview() {
        var container = document.getElementById('imageImportReview');
        if (!container) return;

        var filters = getImageReviewFilters();
        container.innerHTML = '<p class="text-muted text-center p-3">Đang tải dữ liệu review ảnh...</p>';

        fetch('/Home/GetImageImportRows?' + new URLSearchParams(filters).toString())
            .then(function (r) {
                if (!r.ok) throw new Error('Chưa có API review ảnh');
                return r.json();
            })
            .then(function (payload) {
                var rows = extractImageRows(payload);
                if (rows.length === 0) {
                    renderImageImportBatchSummary(container, filters);
                } else {
                    renderImageImportReview(container, rows);
                    hydrateDebtImageQuantities(rows);
                }
            })
            .catch(function () {
                container.innerHTML = '<div class="alert alert-light border small mb-0">Backend review ảnh chưa sẵn sàng. Vẫn có thể upload ảnh và nhập tên khách thủ công ở bảng bên dưới.</div>';
            });
    }

    function renderImageImportBatchSummary(container, filters) {
        fetch('/Home/GetImageImportBatches?' + new URLSearchParams(filters).toString())
            .then(function (r) {
                if (!r.ok) throw new Error('Không có batch ảnh');
                return r.json();
            })
            .then(function (payload) {
                var batches = extractImageBatches(payload);
                if (batches.length === 0) {
                    container.innerHTML = '<div class="alert alert-light border small mb-0">Chưa có dòng ảnh/OCR để review cho ngày đang chọn.</div>';
                    return;
                }

                var html = '<div class="alert alert-warning small mb-2">Chưa có dòng OCR để review. Kiểm tra trạng thái batch bên dưới.</div>';
                html += '<div class="table-responsive"><table class="table table-sm table-bordered mb-0">';
                html += '<thead class="table-light"><tr><th>Ảnh/batch</th><th>Trạng thái</th><th>Raw/lỗi</th></tr></thead><tbody>';
                batches.forEach(function (batch) {
                    html += '<tr>';
                    html += '<td>' + escapeHtml(batch.fileName || batch.imagePath || batch.id || '') + '</td>';
                    html += '<td><span class="badge bg-secondary">' + escapeHtml(batch.status || batch.reviewStatus || 'Chờ xử lý') + '</span></td>';
                    html += '<td class="small text-muted">' + escapeHtml(batch.errorMessage || batch.rawText || batch.message || '') + '</td>';
                    html += '</tr>';
                });
                html += '</tbody></table></div>';
                container.innerHTML = html;
            })
            .catch(function () {
                container.innerHTML = '<div class="alert alert-light border small mb-0">Chưa có dòng ảnh/OCR để review cho ngày đang chọn.</div>';
            });
    }

    function renderImageImportReview(container, rows) {
        if (!rows || rows.length === 0) {
            container.innerHTML = '<div class="alert alert-light border small mb-0">Chưa có dòng ảnh/OCR để review cho ngày đang chọn.</div>';
            return;
        }

        var html = '';
        html += '<div class="table-responsive">';
        html += '<table class="table table-sm table-bordered image-review-table excel-grid" data-static-order="true">';
        html += '<thead class="table-light"><tr>';
        html += '<th style="width:42px">#</th>';
        html += '<th style="min-width:95px">Lái</th>';
        html += '<th style="min-width:120px">Khách</th>';
        html += '<th class="text-end" style="width:80px">SL ảnh</th>';
        html += '<th class="text-end" style="width:96px">Tiền trả</th>';
        html += '<th style="width:92px">Tin cậy</th>';
        html += '<th style="width:104px">Trạng thái</th>';
        html += '<th>Raw</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row, index) {
            var status = normalizeReviewStatus(row.reviewStatus || row.status);
            html += '<tr class="' + getReviewRowClass(status) + '" data-image-row-id="' + escapeHtml(row.id || '') + '">';
            html += '<td>' + escapeHtml(row.imageOrder || row.order || index + 1) + '</td>';
            html += '<td><input class="form-control form-control-sm image-review-input" data-field="tenLai" value="' + escapeHtml(row.tenLai || '') + '" /></td>';
            html += '<td><input class="form-control form-control-sm image-review-input" data-field="tenKhach" value="' + escapeHtml(row.tenKhach || '') + '" list="danhSachTen" /></td>';
            html += '<td><input type="number" step="0.1" class="form-control form-control-sm text-end image-review-input" data-field="soLuongAnh" value="' + escapeHtml(formatNumberInput(row.soLuongAnh)) + '" /></td>';
            html += '<td><input type="number" step="1" class="form-control form-control-sm text-end image-review-input" data-field="soTienTra" value="' + escapeHtml(formatNumberInput(row.soTienTra)) + '" /></td>';
            html += '<td>' + renderConfidence(row.confidence) + '</td>';
            html += '<td><span class="badge ' + getReviewBadgeClass(status) + '">' + getReviewStatusLabel(status) + '</span></td>';
            html += '<td class="small text-muted">' + escapeHtml(row.rawLine || row.rawText || '') + '</td>';
            html += '</tr>';
        });

        html += '</tbody></table></div>';
        container.innerHTML = html;
        setupExcelNavigation(container);
    }

    function applyImageImportReview() {
        var table = document.querySelector('.image-review-table');
        if (!table) {
            alert('Chưa có dữ liệu ảnh để áp dụng!');
            return;
        }

        var rows = Array.from(table.querySelectorAll('tbody tr')).map(function (tr) {
            var item = { id: parseInt(tr.dataset.imageRowId || '0', 10) || 0 };
            tr.querySelectorAll('.image-review-input').forEach(function (input) {
                var field = input.dataset.field;
                if (!field) return;
                item[field] = input.type === 'number' && input.value !== '' ? parseFloat(input.value) : input.value.trim();
            });
            return item;
        });

        if (rows.length === 0) {
            alert('Chưa có dòng review nào để áp dụng!');
            return;
        }

        fetch('/Home/ApplyImageImportReview', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(rows)
        })
            .then(function (r) {
                if (!r.ok) return r.text().then(function (text) { throw new Error(text || 'Không thể áp dụng review'); });
                return r.json();
            })
            .then(function (result) {
                alert('Đã áp dụng review ảnh: ' + (result.updated || rows.length) + ' dòng.');
                window.location.hash = '#tab-hoan-thien';
                window.location.reload();
            })
            .catch(function (err) {
                alert('Chưa áp dụng được review ảnh: ' + err.message);
            });
    }

    function hydrateDebtImageQuantities(existingRows) {
        var table = document.querySelector('.debt-entry-table');
        if (!table) return;

        if (existingRows) {
            applyImageRowsToDebtTable(extractImageRows(existingRows));
            return;
        }

        var url = table.dataset.imageReviewUrl;
        if (!url) return;

        var filters = getImageReviewFilters();
        if (filters.nguonBanHang) {
            url += (url.indexOf('?') >= 0 ? '&' : '?') + 'nguonBanHang=' + encodeURIComponent(filters.nguonBanHang);
        }

        fetch(url)
            .then(function (r) {
                if (!r.ok) throw new Error('Không có dữ liệu ảnh');
                return r.json();
            })
            .then(function (payload) {
                applyImageRowsToDebtTable(extractImageRows(payload));
            })
            .catch(function () {
                updateDebtImageTotals();
            });
    }

    function applyImageRowsToDebtTable(rows) {
        rows.forEach(function (row) {
            var gdId = row.matchedGiaoDichId || row.giaoDichId || row.giaoDichID;
            var tr = gdId
                ? document.querySelector('.giao-dich-row[data-giao-dich-id="' + gdId + '"]')
                : findDebtRowForImageRow(row);
            if (!tr) return;

            var imageQty = parseFlexibleNumber(row.soLuongAnh);
            var imageCell = tr.querySelector('.image-qty-cell');
            if (imageCell && imageQty !== null) {
                imageCell.textContent = formatDisplayNumber(imageQty);
                imageCell.dataset.imageQuantity = imageQty;
            }

            var excelQty = parseFlexibleNumber(tr.dataset.excelQuantity);
            var status = normalizeReviewStatus(row.reviewStatus || row.status);
            var mismatch = imageQty !== null && excelQty !== null && Math.abs(imageQty - excelQty) > 0.05;
            var badge = tr.querySelector('.review-status-badge');

            tr.classList.toggle('image-mismatch', mismatch || status === 'mismatch' || status === 'needs_review');
            if (badge) {
                if (mismatch) {
                    badge.className = 'badge review-status-badge bg-danger';
                    badge.textContent = 'Lệch SL';
                } else if (status === 'matched') {
                    badge.className = 'badge review-status-badge bg-success';
                    badge.textContent = 'Khớp ảnh';
                } else if (status === 'needs_review') {
                    badge.className = 'badge review-status-badge bg-warning text-dark';
                    badge.textContent = 'Cần kiểm';
                }
            }
        });

        updateDebtImageTotals();
    }

    function findDebtRowForImageRow(row) {
        var imageSeller = normalizeMatchText(row.tenLai);
        var imageCustomer = normalizeMatchText(row.tenKhach);
        var imageQty = parseFlexibleNumber(row.soLuongAnh);
        var candidates = Array.from(document.querySelectorAll('.giao-dich-row')).filter(function (tr) {
            var sellerOk = !imageSeller || normalizeMatchText(tr.dataset.tenLai) === imageSeller;
            var customerOk = !imageCustomer || normalizeMatchText(tr.dataset.tenKhach) === imageCustomer;
            return sellerOk && customerOk;
        });

        if (candidates.length === 0) return null;
        if (imageQty === null) return candidates[0];

        candidates.sort(function (a, b) {
            var aQty = parseFlexibleNumber(a.dataset.excelQuantity);
            var bQty = parseFlexibleNumber(b.dataset.excelQuantity);
            return Math.abs((aQty || 0) - imageQty) - Math.abs((bQty || 0) - imageQty);
        });

        return candidates[0];
    }

    function updateDebtImageTotals() {
        var rows = Array.from(document.querySelectorAll('.giao-dich-row'));
        var grandTotal = 0;
        var hasAny = false;
        var bySeller = {};
        var byCustomer = {};

        rows.forEach(function (row) {
            var imageCell = row.querySelector('.image-qty-cell');
            var value = imageCell ? parseFlexibleNumber(imageCell.dataset.imageQuantity) : null;
            if (value === null) return;

            hasAny = true;
            grandTotal += value;
            var seller = row.dataset.tenLai || '';
            var customer = row.dataset.tenKhach || '(chưa có khách mua)';
            bySeller[seller] = (bySeller[seller] || 0) + value;
            byCustomer[seller + '||' + customer] = (byCustomer[seller + '||' + customer] || 0) + value;
        });

        document.querySelectorAll('.image-lai-total').forEach(function (cell) {
            var value = bySeller[cell.dataset.tenLai || ''];
            cell.textContent = value === undefined ? '-' : formatDisplayNumber(value);
        });

        document.querySelectorAll('.image-customer-total').forEach(function (cell) {
            var key = (cell.dataset.tenLai || '') + '||' + (cell.dataset.tenKhach || '');
            var value = byCustomer[key];
            cell.textContent = value === undefined ? '-' : formatDisplayNumber(value);
        });

        var grandCell = document.querySelector('.image-grand-total');
        if (grandCell) grandCell.textContent = hasAny ? formatDisplayNumber(grandTotal) : '-';
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

    function saveBangTraNo() {
        var traInputs = document.querySelectorAll('.tra-no-input');
        var traNoCuInputs = document.querySelectorAll('.tra-no-cu-input');
        var traItems = [];
        var traNoCuItems = [];
        var invalidInput = null;

        traNoCuInputs.forEach(function (input) {
            if (invalidInput) return;
            var current = input.value.trim();
            var original = (input.dataset.original || '').trim();
            if (current === original) return;

            var value = current === '' ? 0 : parseFloat(current);
            if (Number.isNaN(value) || value < 0) {
                invalidInput = input;
                return;
            }

            traNoCuItems.push({
                tenKhach: input.dataset.tenKhach,
                traNoCu: value
            });
        });

        traInputs.forEach(function (input) {
            if (invalidInput) return;
            var current = input.value.trim();
            var original = (input.dataset.original || '').trim();
            if (current === original) return;

            var value = current === '' ? 0 : parseFloat(current);
            if (Number.isNaN(value) || value < 0) {
                invalidInput = input;
                return;
            }

            traItems.push({
                tenKhach: input.dataset.tenKhach,
                ngayTra: input.dataset.ngay,
                soTienTra: value
            });
        });

        if (invalidInput) {
            invalidInput.focus();
            alert('Giá trị nhập không hợp lệ!');
            return;
        }

        if (traItems.length === 0 && traNoCuItems.length === 0) {
            alert('Chưa có ô nào thay đổi!');
            return;
        }

        btnSaveTraNoBang.disabled = true;
        var requests = [];
        if (traNoCuItems.length > 0) {
            requests.push(fetch('/Home/CapNhatBangTraNoCu', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(traNoCuItems)
            }));
        }

        if (traItems.length > 0) {
            requests.push(fetch('/Home/CapNhatBangTraNo', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(traItems)
            }));
        }

        Promise.all(requests)
            .then(function (responses) {
                var failed = responses.find(function (r) { return !r.ok; });
                if (failed) return failed.text().then(function (text) { throw new Error(text || 'Không thể lưu'); });
                return Promise.all(responses.map(function (r) { return r.json(); }));
            })
            .then(function (results) {
                var updated = results.reduce(function (sum, result) {
                    return sum + (result.updated || 0);
                }, 0);
                alert('Đã cập nhật ' + updated + ' ô!');
                window.location.hash = '#tab-tra-no';
                window.location.reload();
            })
            .catch(function (err) {
                alert('Lỗi kết nối: ' + err.message);
                btnSaveTraNoBang.disabled = false;
            });
    }

    function setupDebtPaidFullToggles(root) {
        root.addEventListener('change', function (e) {
            var toggle = e.target.closest('.debt-paid-full-toggle');
            if (!toggle) return;

            var cell = toggle.closest('.debt-pay-cell');
            if (!cell) return;

            var input = cell.querySelector('.tra-no-input');
            if (!input) return;

            if (toggle.checked) {
                input.value = toggle.dataset.fillAmount || '0';
            } else {
                input.value = input.dataset.original || '';
            }

            markChangedInput(input);
        });
    }

    function setupExcelNavigation(root) {
        root.querySelectorAll('.excel-grid input, .excel-grid select, .excel-grid textarea').forEach(function (input) {
            if (input.dataset.excelReady === 'true') return;
            if (input.type === 'hidden' || input.type === 'file' || input.type === 'button' || input.type === 'submit') return;

            input.dataset.excelReady = 'true';
            if (!input.dataset.original) input.dataset.original = input.value || '';

            input.addEventListener('keydown', handleExcelKeydown);
            input.addEventListener('input', function () { markChangedInput(input); });
            input.addEventListener('change', function () { markChangedInput(input); });
            input.addEventListener('focus', function () {
                input.closest('td, .col-md-1, .col-md-2, .col-md-3, .col-md-4')?.classList.add('excel-active-cell');
            });
            input.addEventListener('blur', function () {
                input.closest('td, .col-md-1, .col-md-2, .col-md-3, .col-md-4')?.classList.remove('excel-active-cell');
            });
        });
    }

    function handleExcelKeydown(e) {
        if (e.key === 'Escape') {
            e.preventDefault();
            e.currentTarget.value = e.currentTarget.dataset.original || '';
            markChangedInput(e.currentTarget);
            return;
        }

        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Enter'].includes(e.key)) return;
        if (e.key === 'Enter') e.preventDefault();
        if ((e.key === 'ArrowLeft' || e.key === 'ArrowRight') && shouldLetTextCursorMove(e.currentTarget, e.key)) return;

        var next = findNextExcelInput(e.currentTarget, e.key === 'Enter' ? 'ArrowDown' : e.key);
        if (!next) return;

        e.preventDefault();
        next.focus();
        if (typeof next.select === 'function' && next.type !== 'checkbox') next.select();
    }

    function findNextExcelInput(current, direction) {
        var table = current.closest('table');
        if (table) {
            var currentCell = current.closest('td, th');
            var currentRow = current.closest('tr');
            if (!currentCell || !currentRow) return null;

            var rowIndex = currentRow.rowIndex;
            var cellIndex = currentCell.cellIndex;
            var targetCell = null;

            if (direction === 'ArrowLeft') targetCell = currentRow.cells[cellIndex - 1];
            if (direction === 'ArrowRight') targetCell = currentRow.cells[cellIndex + 1];
            if (direction === 'ArrowUp' && table.rows[rowIndex - 1]) targetCell = table.rows[rowIndex - 1].cells[cellIndex];
            if (direction === 'ArrowDown' && table.rows[rowIndex + 1]) targetCell = table.rows[rowIndex + 1].cells[cellIndex];

            return targetCell ? firstEditableInput(targetCell) : findAdjacentInput(current, direction);
        }

        return findAdjacentInput(current, direction);
    }

    function findAdjacentInput(current, direction) {
        var grid = current.closest('.excel-grid') || document;
        var inputs = getExcelInputs(grid);
        var currentIndex = inputs.indexOf(current);
        if (currentIndex < 0) return null;

        if (direction === 'ArrowLeft' || direction === 'ArrowUp') return inputs[currentIndex - 1] || null;
        return inputs[currentIndex + 1] || null;
    }

    function firstEditableInput(root) {
        return getExcelInputs(root)[0] || null;
    }

    function getExcelInputs(root) {
        return Array.from(root.querySelectorAll('input, select, textarea')).filter(function (input) {
            if (input.disabled || input.readOnly) return false;
            if (input.type === 'hidden' || input.type === 'file' || input.type === 'button' || input.type === 'submit') return false;
            return input.offsetParent !== null;
        });
    }

    function shouldLetTextCursorMove(input, key) {
        if (!['text', 'search'].includes(input.type) && input.tagName !== 'TEXTAREA') return false;
        var start = input.selectionStart;
        var end = input.selectionEnd;
        if (start === null || end === null || start !== end) return false;
        if (key === 'ArrowLeft') return start > 0;
        if (key === 'ArrowRight') return end < input.value.length;
        return false;
    }

    function markChangedInput(input) {
        var changed = (input.value || '') !== (input.dataset.original || '');
        input.classList.toggle('cell-changed', changed);
    }

    function getCurrentDateValue() {
        var ngay = document.querySelector('input[name="ngay"]');
        return ngay ? ngay.value : '';
    }

    function getImageReviewFilters() {
        var loai = document.getElementById('imageLoaiImport');
        var nguon = document.getElementById('imageNguonBanHang');
        return {
            ngay: getCurrentDateValue(),
            loaiImport: loai ? loai.value : 'NhapNoMoi',
            nguonBanHang: nguon ? nguon.value : ''
        };
    }

    function inferImageImportFromFiles(files) {
        if (!files || files.length === 0) return;

        var firstName = (files[0].name || '').toLowerCase().trim();
        var loai = document.getElementById('imageLoaiImport');
        var nguon = document.getElementById('imageNguonBanHang');

        if (firstName.startsWith('bh1')) {
            if (loai) loai.value = 'NhapNoMoi';
            if (nguon) nguon.value = 'BH1';
        } else if (firstName.startsWith('bh2')) {
            if (loai) loai.value = 'NhapNoMoi';
            if (nguon) nguon.value = 'BH2';
        } else if (firstName.startsWith('xa1')) {
            if (loai) loai.value = 'TraNoHomNay';
            if (nguon) nguon.value = 'BH1';
        } else if (firstName.startsWith('xa2')) {
            if (loai) loai.value = 'TraNoHomNay';
            if (nguon) nguon.value = 'BH2';
        }

        loadImageImportReview();
    }

    function setImageImportBusy(isBusy) {
        var button = document.getElementById('btnImportImageBatch');
        var status = document.getElementById('imageImportStatus');
        if (!button) return;

        if (!button.dataset.originalHtml) button.dataset.originalHtml = button.innerHTML;
        button.disabled = isBusy;
        button.innerHTML = isBusy
            ? '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Đang xử lý'
            : button.dataset.originalHtml;

        if (status) {
            status.classList.toggle('d-none', !isBusy);
            status.textContent = isBusy ? 'Đang gửi ảnh và đọc dữ liệu...' : '';
        }
    }

    function extractImageRows(payload) {
        if (!payload) return [];
        if (Array.isArray(payload)) return payload;
        if (Array.isArray(payload.rows)) return payload.rows;
        if (Array.isArray(payload.items)) return payload.items;
        if (Array.isArray(payload.data)) return payload.data;
        if (Array.isArray(payload.batches)) {
            return payload.batches.reduce(function (all, batch) {
                return all.concat(extractImageRows(batch));
            }, []);
        }
        return [];
    }

    function extractImageBatches(payload) {
        if (!payload) return [];
        if (Array.isArray(payload)) return payload;
        if (Array.isArray(payload.batches)) return payload.batches;
        if (Array.isArray(payload.items)) return payload.items;
        if (Array.isArray(payload.data)) return payload.data;
        return [];
    }

    function normalizeReviewStatus(status) {
        return (status || '').toString().trim().toLowerCase().replace('-', '_');
    }

    function getReviewRowClass(status) {
        if (status === 'matched') return 'table-success';
        if (status === 'mismatch') return 'table-danger';
        if (status === 'needs_review' || status === 'need_review') return 'table-warning';
        return '';
    }

    function getReviewBadgeClass(status) {
        if (status === 'matched') return 'bg-success';
        if (status === 'mismatch') return 'bg-danger';
        if (status === 'needs_review' || status === 'need_review') return 'bg-warning text-dark';
        return 'bg-secondary';
    }

    function getReviewStatusLabel(status) {
        if (status === 'matched') return 'Khớp';
        if (status === 'mismatch') return 'Lệch';
        if (status === 'needs_review' || status === 'need_review') return 'Cần kiểm';
        return 'Chờ kiểm';
    }

    function renderConfidence(confidence) {
        var value = parseFlexibleNumber(confidence);
        if (value === null) return '<span class="text-muted">-</span>';
        var percent = value <= 1 ? Math.round(value * 100) : Math.round(value);
        var cls = percent >= 85 ? 'bg-success' : percent >= 65 ? 'bg-warning text-dark' : 'bg-danger';
        return '<span class="badge ' + cls + '">' + percent + '%</span>';
    }

    function formatNumberInput(value) {
        var number = parseFlexibleNumber(value);
        return number === null || number === 0 ? '' : String(number);
    }

    function parseFlexibleNumber(value) {
        if (value === null || value === undefined || value === '') return null;
        if (typeof value === 'number') return Number.isNaN(value) ? null : value;
        var normalized = String(value).trim().replace(/\s/g, '');
        if (!normalized) return null;
        if (normalized.indexOf(',') >= 0 && normalized.indexOf('.') >= 0) {
            normalized = normalized.replace(/\./g, '').replace(',', '.');
        } else {
            normalized = normalized.replace(',', '.');
        }
        var number = parseFloat(normalized);
        return Number.isNaN(number) ? null : number;
    }

    function normalizeMatchText(value) {
        return String(value || '')
            .trim()
            .toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '')
            .replace(/đ/g, 'd')
            .replace(/\s+/g, ' ');
    }

    function formatDisplayNumber(value) {
        return value.toLocaleString('vi-VN', { maximumFractionDigits: 1 });
    }

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function setupSortableTables(root) {
        root.querySelectorAll('table').forEach(function (table) {
            if (table.dataset.sortReady === 'true') return;
            if (table.dataset.staticOrder === 'true') return;

            var headers = table.querySelectorAll('thead th');
            headers.forEach(function (th) {
                if (th.dataset.noSort === 'true') return;
                if (th.colSpan > 1 || !th.textContent.trim()) return;

                var columnIndex = getHeaderColumnIndex(th);
                if (columnIndex < 0) return;

                th.classList.add('sortable-header');
                th.dataset.sortColumnIndex = columnIndex;
                th.setAttribute('role', 'button');
                th.setAttribute('tabindex', '0');
                th.title = 'Bấm để sắp xếp';
            });

            table.dataset.sortReady = 'true';
        });
    }

    function handleSortableHeaderClick(e) {
        var th = e.target.closest('th.sortable-header');
        if (!th) return;

        e.preventDefault();
        toggleTableSort(th);
    }

    function handleSortableHeaderKeydown(e) {
        if (e.key !== 'Enter' && e.key !== ' ') return;

        var th = e.target.closest('th.sortable-header');
        if (!th) return;

        e.preventDefault();
        toggleTableSort(th);
    }

    function toggleTableSort(th) {
        var table = th.closest('table');
        if (!table) return;

        var columnIndex = parseInt(th.dataset.sortColumnIndex || '-1', 10);
        if (columnIndex < 0) return;

        var nextDirection = th.dataset.sortDirection === 'asc' ? 'desc' : 'asc';
        sortTable(table, columnIndex, nextDirection);

        table.querySelectorAll('thead th').forEach(function (other) {
            other.classList.remove('sort-asc', 'sort-desc');
            delete other.dataset.sortDirection;
        });

        th.dataset.sortDirection = nextDirection;
        th.classList.add(nextDirection === 'asc' ? 'sort-asc' : 'sort-desc');
    }

    function getHeaderColumnIndex(th) {
        var rows = Array.from(th.closest('thead').rows);
        var grid = [];

        for (var rowIndex = 0; rowIndex < rows.length; rowIndex++) {
            var row = rows[rowIndex];
            grid[rowIndex] = grid[rowIndex] || [];
            var colIndex = 0;

            Array.from(row.cells).forEach(function (cell) {
                while (grid[rowIndex][colIndex]) colIndex++;

                for (var r = 0; r < cell.rowSpan; r++) {
                    for (var c = 0; c < cell.colSpan; c++) {
                        grid[rowIndex + r] = grid[rowIndex + r] || [];
                        grid[rowIndex + r][colIndex + c] = cell;
                    }
                }

                if (cell === th) {
                    th.dataset.sortColumnIndex = colIndex;
                }

                colIndex += cell.colSpan;
            });
        }

        return parseInt(th.dataset.sortColumnIndex || '-1', 10);
    }

    function sortTable(table, columnIndex, direction) {
        var tbody = table.tBodies[0];
        if (!tbody) return;

        var rows = Array.from(tbody.rows);
        var sortableRows = rows.filter(function (row) {
            return row.cells.length > columnIndex && !row.classList.contains('table-info');
        });
        var summaryRows = rows.filter(function (row) {
            return !sortableRows.includes(row);
        });
        var multiplier = direction === 'asc' ? 1 : -1;

        sortableRows.sort(function (a, b) {
            var left = getSortValue(a.cells[columnIndex]);
            var right = getSortValue(b.cells[columnIndex]);

            if (left.type === 'number' && right.type === 'number') {
                return (left.value - right.value) * multiplier;
            }

            return left.value.localeCompare(right.value, 'vi', {
                numeric: true,
                sensitivity: 'base'
            }) * multiplier;
        });

        sortableRows.concat(summaryRows).forEach(function (row) {
            tbody.appendChild(row);
        });
    }

    function getSortValue(cell) {
        if (!cell) return { type: 'text', value: '' };

        var input = cell.querySelector('input, select, textarea');
        var raw = (cell.dataset.sortValue || (input ? input.value : cell.textContent)).trim();
        var numeric = raw.replace(/\s/g, '');

        if (/^-?\d+([,.]\d+)?$/.test(numeric) || /^-?\d{1,3}([,.]\d{3})+([,.]\d+)?$/.test(numeric)) {
            var normalized = numeric;
            if (normalized.indexOf(',') >= 0 && normalized.indexOf('.') >= 0) {
                normalized = normalized.replace(/\./g, '').replace(',', '.');
            } else {
                normalized = normalized.replace(/,/g, '');
            }

            var numberValue = parseFloat(normalized);
            if (!Number.isNaN(numberValue)) {
                return { type: 'number', value: numberValue };
            }
        }

        return { type: 'text', value: raw };
    }
});
