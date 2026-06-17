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

    // ===== SO SÁNH 2 NGUỒN DỮ LIỆU =====

    var compareState = { data: null, userMatches: [] };

    var btnLoadCompare = document.getElementById('btnLoadCompare');
    if (btnLoadCompare) {
        btnLoadCompare.addEventListener('click', loadCompareData);
        document.getElementById('btnClaudeMatch').addEventListener('click', runClaudeMatch);
    }

    function loadCompareData() {
        var ngay = (document.querySelector('input[name="ngay"]') || {}).value
            || new Date().toISOString().slice(0, 10);
        var loai = document.getElementById('compareLoai').value;
        var nguon = document.getElementById('compareNguon').value;
        var container = document.getElementById('compareResult');
        if (!container) return;

        container.innerHTML = '<p class="text-center text-muted"><span class="spinner-border spinner-border-sm me-1"></span>Đang tải...</p>';

        var url = '/Home/GetCompareData?ngay=' + ngay + '&loaiImport=' + loai + (nguon ? '&nguonBanHang=' + nguon : '');
        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (data) {
                compareState.data = data;
                compareState.userMatches = (data.matches || []).slice();
                renderCompareTable(container, data);
                var btn = document.getElementById('btnClaudeMatch');
                if (btn) btn.disabled = false;
            })
            .catch(function (err) {
                container.innerHTML = '<p class="text-danger small">Lỗi tải dữ liệu: ' + err + '</p>';
            });
    }

    function renderCompareTable(container, data) {
        var loai = data.loai || 'NhapNoMoi';
        var isTraNo = loai === 'TraNoHomNay';
        var excel = data.excel || [];
        var image = data.image || [];
        var matches = compareState.userMatches || [];

        var matchedExcelIds = new Set(matches.map(function (m) { return m.excelId; }));
        var matchedImageIds = new Set(matches.map(function (m) { return m.imageId; }));
        var unmatchedExcel = excel.filter(function (e) { return !matchedExcelIds.has(e.id); });
        var unmatchedImage = image.filter(function (i) { return !matchedImageIds.has(i.id); });

        var amtLabel = isTraNo ? 'Tiền (đ)' : 'SL (kg)';

        var html = '<table class="table table-sm table-bordered small mb-0">';
        html += '<thead class="table-dark"><tr>';
        html += '<th>Tên Excel</th><th>Tên Ảnh OCR</th>';
        html += '<th style="width:70px">Khớp tên</th>';
        html += '<th>' + amtLabel + ' Excel</th>';
        html += '<th>' + amtLabel + ' Ảnh</th>';
        html += '<th style="width:80px">Chênh</th>';
        html += '</tr></thead><tbody>';

        matches.forEach(function (m) {
            var exRow = excel.find(function (e) { return e.id === m.excelId; });
            var imRow = image.find(function (i) { return i.id === m.imageId; });
            if (!exRow || !imRow) return;

            var sim = Math.round((m.nameSimilarity || 0) * 100);
            var simCls = sim >= 90 ? 'bg-success' : sim >= 65 ? 'bg-warning text-dark' : 'bg-danger';
            var rowCls = m.hasDiscrepancy ? 'table-danger' : (m.matchMethod === 'suggest' ? 'table-warning' : '');
            var amtEx = isTraNo ? exRow.soTienTra : exRow.soLuong;
            var amtIm = isTraNo ? imRow.soTienTra : imRow.soLuongAnh;
            var diff = m.amountDiff;

            html += '<tr class="' + rowCls + '">';
            html += '<td>' + escapeHtml(exRow.tenKhach || '') + '</td>';
            html += '<td>' + escapeHtml(imRow.tenKhach || '')
                + (m.matchMethod === 'claude' ? ' <span class="badge bg-info">AI</span>' : '') + '</td>';
            html += '<td><span class="badge ' + simCls + '">' + sim + '%</span></td>';
            html += '<td class="text-end">' + (amtEx != null ? formatDisplayNumber(amtEx) : '<span class="text-muted">-</span>') + '</td>';
            html += '<td class="text-end">' + (amtIm != null ? formatDisplayNumber(amtIm) : '<span class="text-muted">-</span>') + '</td>';
            html += '<td class="text-end">' + (diff != null && diff > 0
                ? '<span class="text-danger fw-bold">' + formatDisplayNumber(diff) + '</span>'
                : '<span class="text-success">OK</span>') + '</td>';
            html += '</tr>';
        });

        unmatchedExcel.forEach(function (e) {
            var amt = isTraNo ? e.soTienTra : e.soLuong;
            html += '<tr class="table-secondary">';
            html += '<td>' + escapeHtml(e.tenKhach || '') + '</td>';
            html += '<td><em class="text-muted">— không có ảnh tương ứng —</em></td>';
            html += '<td>-</td>';
            html += '<td class="text-end">' + (amt != null ? formatDisplayNumber(amt) : '-') + '</td>';
            html += '<td>-</td><td>-</td></tr>';
        });

        unmatchedImage.forEach(function (img) {
            var amt = isTraNo ? img.soTienTra : img.soLuongAnh;
            html += '<tr class="table-secondary">';
            html += '<td><em class="text-muted">— không có Excel tương ứng —</em></td>';
            html += '<td>' + escapeHtml(img.tenKhach || '') + '</td>';
            html += '<td>-</td><td>-</td>';
            html += '<td class="text-end">' + (amt != null ? formatDisplayNumber(amt) : '-') + '</td>';
            html += '<td>-</td></tr>';
        });

        html += '</tbody></table>';

        var discCnt = matches.filter(function (m) { return m.hasDiscrepancy; }).length;
        var suggestCnt = matches.filter(function (m) { return m.matchMethod === 'suggest'; }).length;
        var summary = '<div class="d-flex flex-wrap gap-2 mb-2 small">';
        summary += '<span class="badge bg-success">Khớp chắc: ' + matches.filter(function (m) { return m.matchMethod === 'auto'; }).length + '</span>';
        if (suggestCnt) summary += '<span class="badge bg-warning text-dark">Cần xác nhận: ' + suggestCnt + '</span>';
        if (discCnt) summary += '<span class="badge bg-danger">Lệch số liệu: ' + discCnt + '</span>';
        if (unmatchedExcel.length) summary += '<span class="badge bg-secondary">Excel chưa khớp: ' + unmatchedExcel.length + '</span>';
        if (unmatchedImage.length) summary += '<span class="badge bg-secondary">Ảnh chưa khớp: ' + unmatchedImage.length + '</span>';
        summary += '</div>';

        container.innerHTML = summary + html;
    }

    function runClaudeMatch() {
        var data = compareState.data;
        if (!data) return;

        var confirmed = (compareState.userMatches || []).filter(function (m) { return (m.nameSimilarity || 0) >= 0.9; });
        var confirmedExcelIds = new Set(confirmed.map(function (m) { return m.excelId; }));
        var confirmedImageIds = new Set(confirmed.map(function (m) { return m.imageId; }));

        var pendingExcel = (data.excel || []).filter(function (e) { return !confirmedExcelIds.has(e.id); });
        var pendingImage = (data.image || []).filter(function (i) { return !confirmedImageIds.has(i.id); });

        if (pendingExcel.length === 0 && pendingImage.length === 0) {
            alert('Tất cả tên đã được khớp với độ tin cậy cao (>= 90%).');
            return;
        }

        var btn = document.getElementById('btnClaudeMatch');
        var originalHtml = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Claude đang xử lý...';

        fetch('/Home/MatchNamesWithClaude', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                excelNames: pendingExcel.map(function (e) { return e.tenKhach || ''; }),
                imageNames: pendingImage.map(function (i) { return i.tenKhach || ''; })
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            btn.disabled = false;
            btn.innerHTML = originalHtml;

            if (!result.success) {
                alert('Lỗi Claude: ' + (result.error || 'Không xác định'));
                return;
            }

            var loai = data.loai || 'NhapNoMoi';
            var isTraNo = loai === 'TraNoHomNay';
            var newMatches = confirmed.slice();

            (result.matches || []).forEach(function (cm) {
                var exRow = pendingExcel[cm.excelIndex];
                var imRow = pendingImage[cm.imageIndex];
                if (!exRow || !imRow) return;

                var amtEx = isTraNo ? exRow.soTienTra : exRow.soLuong;
                var amtIm = isTraNo ? imRow.soTienTra : imRow.soLuongAnh;
                var diff = (amtEx != null && amtIm != null) ? Math.abs(amtEx - amtIm) : null;

                newMatches.push({
                    excelId: exRow.id, imageId: imRow.id,
                    excelName: exRow.tenKhach, imageName: imRow.tenKhach,
                    nameSimilarity: cm.confidence || 0.7,
                    matchMethod: 'claude',
                    amountExcel: amtEx, amountImage: amtIm, amountDiff: diff,
                    hasDiscrepancy: diff != null && diff > (isTraNo ? 1000 : 0.5)
                });
            });

            compareState.userMatches = newMatches;
            var container = document.getElementById('compareResult');
            if (container) renderCompareTable(container, data);
        })
        .catch(function (err) {
            btn.disabled = false;
            btn.innerHTML = originalHtml;
            alert('Lỗi kết nối: ' + err);
        });
    }

    // ===== DUAL PANEL: So sánh & hoàn thiện 2 nguồn =====
    (function () {
        var dp = { excelRows: [], imageRows: [], matches: [] };

        function dpLoai() { var e = document.getElementById('dpLoai'); return e ? e.value : 'NhapNoMoi'; }
        function dpNguon() { var e = document.getElementById('dpNguon'); return e ? e.value : 'BH1'; }
        function dpNgay() {
            var e = document.querySelector('input[name="ngay"]');
            return e ? e.value : new Date().toISOString().slice(0, 10);
        }

        var btnSave = document.getElementById('dpBtnSave');
        if (!btnSave) return;

        document.getElementById('dpExcelFile').addEventListener('change', function () {
            if (this.files && this.files[0]) dpUploadExcel(this.files[0]);
            this.value = '';
        });
        document.getElementById('dpImageFiles').addEventListener('change', function () {
            if (this.files && this.files.length) dpUploadImages(Array.from(this.files));
            this.value = '';
        });
        document.getElementById('dpExcelReset').addEventListener('click', function () {
            dp.excelRows = []; dp.matches = [];
            dpPlaceholder('dpExcelTable', 'primary', 'bi-file-earmark-excel', 'Upload file Excel để bắt đầu');
            document.getElementById('dpExcelCount').textContent = '0 dòng';
            dpMsg('dpExcelMsg', '', ''); dpHideSummary(); dpUpdateSave();
        });
        document.getElementById('dpImageReset').addEventListener('click', function () {
            dp.imageRows = []; dp.matches = [];
            dpPlaceholder('dpImageTable', 'success', 'bi-camera', 'Upload ảnh sổ tay (hỗ trợ nhiều ảnh)');
            document.getElementById('dpImageCount').textContent = '0 dòng';
            dpMsg('dpImageMsg', '', ''); dpHideSummary(); dpUpdateSave();
        });
        document.getElementById('dpBtnReset').addEventListener('click', function () {
            dp.excelRows = []; dp.imageRows = []; dp.matches = [];
            dpPlaceholder('dpExcelTable', 'primary', 'bi-file-earmark-excel', 'Upload file Excel để bắt đầu');
            dpPlaceholder('dpImageTable', 'success', 'bi-camera', 'Upload ảnh sổ tay (hỗ trợ nhiều ảnh)');
            document.getElementById('dpExcelCount').textContent = '0 dòng';
            document.getElementById('dpImageCount').textContent = '0 dòng';
            dpMsg('dpExcelMsg', '', ''); dpMsg('dpImageMsg', '', ''); dpMsg('dpSaveMsg', '', '');
            dpHideSummary(); dpUpdateSave();
            var b = document.getElementById('dpBtnSave');
            b.className = 'btn btn-sm btn-success'; b.innerHTML = '<i class="bi bi-check-circle"></i> Xác nhận &amp; Lưu';
        });
        btnSave.addEventListener('click', dpSave);
        document.getElementById('dpLoai').addEventListener('change', function () {
            if (dp.excelRows.length || dp.imageRows.length) dpRunCompare();
        });

        // ---- Fuzzy matching ----
        function dpNorm(s) {
            if (!s) return '';
            return s.trim().toLowerCase().replace(/đ/g, 'd').replace(/Đ/g, 'd')
                .normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/\s+/g, ' ').trim();
        }
        function dpLev(a, b) {
            var prev = Array.from({ length: b.length + 1 }, function (_, j) { return j; });
            for (var i = 1; i <= a.length; i++) {
                var cur = [i];
                for (var j = 1; j <= b.length; j++)
                    cur[j] = a[i-1] === b[j-1] ? prev[j-1] : 1 + Math.min(prev[j], cur[j-1], prev[j-1]);
                prev = cur;
            }
            return prev[b.length];
        }
        function dpSim(a, b) {
            var na = dpNorm(a), nb = dpNorm(b);
            if (!na || !nb) return 0;
            if (na === nb) return 1;
            if (na.includes(nb) || nb.includes(na)) return 0.85;
            var d = dpLev(na, nb);
            return 1 - d / Math.max(na.length, nb.length);
        }
        function dpMatch(excelRows, imageRows) {
            var matches = excelRows.map(function (_, ei) { return { excelIdx: ei, imageIdx: -1, sim: 0 }; });
            var used = new Set();
            excelRows.forEach(function (er, ei) {
                var best = -1, bestSim = 0;
                imageRows.forEach(function (ir, ii) {
                    if (used.has(ii)) return;
                    var s = dpSim(er.tenKhach, ir.tenKhach);
                    if (s > bestSim) { bestSim = s; best = ii; }
                });
                if (best >= 0 && bestSim >= 0.45) { matches[ei] = { excelIdx: ei, imageIdx: best, sim: bestSim }; used.add(best); }
            });
            return matches;
        }

        // ---- Upload Excel ----
        function dpUploadExcel(file) {
            dpMsg('dpExcelMsg', 'info', '<span class="spinner-border spinner-border-sm me-1"></span>Đang đọc Excel...');
            var fd = new FormData();
            fd.append('file', file); fd.append('ngay', dpNgay()); fd.append('loai', dpLoai());
            fetch('/Home/ParseExcelPreview', { method: 'POST', body: fd })
                .then(function (r) { return r.json(); })
                .then(function (d) {
                    if (d.ok) {
                        dp.excelRows = d.rows || [];
                        document.getElementById('dpExcelCount').textContent = dp.excelRows.length + ' dòng';
                        dpMsg('dpExcelMsg', dp.excelRows.length ? 'success' : 'warning',
                            dp.excelRows.length ? 'Đọc được ' + dp.excelRows.length + ' dòng.' : 'Không đọc được dòng nào.');
                        dpRunCompare();
                    } else {
                        dpMsg('dpExcelMsg', 'danger', 'Lỗi: ' + (d.error || 'Không xác định'));
                    }
                }).catch(function (e) { dpMsg('dpExcelMsg', 'danger', 'Lỗi: ' + e.message); });
        }

        // ---- Upload Images (one by one) ----
        async function dpUploadImages(files) {
            dp.imageRows = [];
            for (var i = 0; i < files.length; i++) {
                dpMsg('dpImageMsg', 'info',
                    '<span class="spinner-border spinner-border-sm me-1"></span>OCR ảnh ' + (i + 1) + '/' + files.length + '...');
                var fd = new FormData();
                fd.append('file', files[i]); fd.append('loai', dpLoai());
                try {
                    var r = await fetch('/Home/ParseImagePreview', { method: 'POST', body: fd });
                    var d = await r.json();
                    if (d.ok && d.rows) dp.imageRows = dp.imageRows.concat(d.rows);
                    if (!d.ok && d.error) dpMsg('dpImageMsg', 'warning', 'Ảnh ' + files[i].name + ': ' + d.error);
                } catch (e) { dpMsg('dpImageMsg', 'warning', 'Lỗi ảnh ' + files[i].name + ': ' + e.message); }
            }
            document.getElementById('dpImageCount').textContent = dp.imageRows.length + ' dòng';
            dpMsg('dpImageMsg', dp.imageRows.length ? 'success' : 'warning',
                dp.imageRows.length
                    ? 'Đọc được ' + dp.imageRows.length + ' dòng từ ' + files.length + ' ảnh.'
                    : 'Không đọc được dòng nào.');
            dpRunCompare();
        }

        // ---- Compare & render ----
        function dpRunCompare() {
            dp.matches = dpMatch(dp.excelRows, dp.imageRows);
            dpRenderExcel();
            dpRenderImage();
            dpRenderSummary();
            dpUpdateSave();
        }

        function dpRenderExcel() {
            var el = document.getElementById('dpExcelTable');
            if (!el) return;
            if (!dp.excelRows.length) { dpPlaceholder('dpExcelTable', 'primary', 'bi-file-earmark-excel', 'Upload file Excel để bắt đầu'); return; }
            var isT = dpLoai() === 'TraNoHomNay';
            var h = '<table class="table table-sm table-bordered mb-0" style="font-size:.84rem">';
            h += '<thead class="table-primary" style="position:sticky;top:0;z-index:1"><tr>';
            h += '<th style="width:28px">#</th>';
            if (!isT) h += '<th>Lái</th>';
            h += '<th>Khách mua</th>';
            if (!isT) { h += '<th style="width:72px">SL(kg)</th><th style="width:72px">Giá</th><th style="width:88px">T.Tiền</th>'; }
            else { h += '<th style="width:110px">Số tiền trả</th>'; }
            h += '<th style="width:88px">Trạng thái</th></tr></thead><tbody>';

            dp.excelRows.forEach(function (row, i) {
                var m = dp.matches[i] || { excelIdx: i, imageIdx: -1, sim: 0 };
                var ir = m.imageIdx >= 0 ? dp.imageRows[m.imageIdx] : null;
                var diff = ir && (isT
                    ? Math.abs((+row.soTienTra || 0) - (+ir.soTienTra || 0)) > 1000
                    : Math.abs((+row.soLuong || 0) - (+ir.soLuongAnh || 0)) > 0.1);
                var trCls = ir ? (diff ? 'table-warning' : '') : 'table-light';
                var badge = ir
                    ? (diff ? '<span class="badge bg-warning text-dark">⚠ Lệch</span>' : '<span class="badge bg-success">✓ Khớp</span>')
                    : '<span class="badge bg-secondary">Chưa khớp</span>';
                var peer = ir ? '<br><small class="text-muted">↔ ảnh #' + (m.imageIdx + 1) + '</small>' : '';
                var slV = (diff && ir && !isT && ir.soLuongAnh != null) ? ir.soLuongAnh : (row.soLuong || '');
                var tvV = (diff && ir && isT && ir.soTienTra != null) ? ir.soTienTra : (row.soTienTra || '');

                h += '<tr class="' + trCls + '">';
                h += '<td class="text-muted small align-middle">' + (i + 1) + '</td>';
                if (!isT) h += '<td><input class="form-control form-control-sm dp-ex" data-idx="' + i + '" data-f="tenLai" value="' + dpEsc(row.tenLai || '') + '"></td>';
                h += '<td><input class="form-control form-control-sm dp-ex" data-idx="' + i + '" data-f="tenKhach" value="' + dpEsc(row.tenKhach || '') + '"></td>';
                if (!isT) {
                    h += '<td><input type="number" step="0.1" class="form-control form-control-sm dp-ex' + (diff ? ' border-danger border-2' : '') + '" data-idx="' + i + '" data-f="soLuong" value="' + slV + '"></td>';
                    h += '<td><input type="number" class="form-control form-control-sm dp-ex" data-idx="' + i + '" data-f="gia" value="' + (row.gia || '') + '"></td>';
                    h += '<td><input type="number" class="form-control form-control-sm dp-ex" data-idx="' + i + '" data-f="thanhTien" value="' + (row.thanhTien || '') + '"></td>';
                } else {
                    h += '<td><input type="number" class="form-control form-control-sm dp-ex' + (diff ? ' border-danger border-2' : '') + '" data-idx="' + i + '" data-f="soTienTra" value="' + tvV + '"></td>';
                }
                h += '<td class="align-middle small">' + badge + peer + '</td></tr>';
            });
            h += '</tbody></table>';
            el.innerHTML = h;
            el.querySelectorAll('.dp-ex').forEach(function (inp) {
                inp.addEventListener('change', function () {
                    var idx = +this.dataset.idx;
                    if (dp.excelRows[idx]) dp.excelRows[idx][this.dataset.f] = this.value;
                });
            });
        }

        function dpRenderImage() {
            var el = document.getElementById('dpImageTable');
            if (!el) return;
            if (!dp.imageRows.length) { dpPlaceholder('dpImageTable', 'success', 'bi-camera', 'Upload ảnh sổ tay (hỗ trợ nhiều ảnh)'); return; }
            var isT = dpLoai() === 'TraNoHomNay';
            var rev = {};
            dp.matches.forEach(function (m) { if (m.imageIdx >= 0) rev[m.imageIdx] = m.excelIdx; });

            var h = '<table class="table table-sm table-bordered mb-0" style="font-size:.84rem">';
            h += '<thead class="table-success" style="position:sticky;top:0;z-index:1"><tr>';
            h += '<th style="width:28px">#</th><th style="width:50px">Conf</th><th>Tên khách (OCR)</th>';
            h += '<th style="width:' + (isT ? 100 : 72) + 'px">' + (isT ? 'Số tiền' : 'SL ảnh') + '</th>';
            h += '<th style="width:68px">Khớp</th></tr></thead><tbody>';

            dp.imageRows.forEach(function (ir, i) {
                var exIdx = rev.hasOwnProperty(i) ? rev[i] : -1;
                var conf = +(ir.confidence || 0);
                var cBg = conf >= 0.85 ? 'bg-success' : conf >= 0.7 ? 'bg-warning text-dark' : 'bg-danger';
                var amt = isT
                    ? (ir.soTienTra != null ? Math.round(+ir.soTienTra).toLocaleString('vi-VN') + 'đ' : '—')
                    : (ir.soLuongAnh != null ? ir.soLuongAnh + ' kg' : '—');
                var mBadge = exIdx >= 0
                    ? '<span class="badge bg-primary">Excel #' + (exIdx + 1) + '</span>'
                    : '<span class="badge bg-info text-dark">Chưa</span>';

                h += '<tr class="' + (exIdx < 0 ? 'table-info' : '') + '">';
                h += '<td class="text-muted small align-middle">' + (i + 1) + '</td>';
                h += '<td><span class="badge ' + cBg + '">' + Math.round(conf * 100) + '%</span></td>';
                h += '<td class="align-middle">' + dpEsc(ir.tenKhach || '—');
                if (ir.tenLai) h += '<br><small class="text-muted">Lái: ' + dpEsc(ir.tenLai) + '</small>';
                h += '</td><td class="align-middle text-end">' + amt + '</td>';
                h += '<td class="align-middle">' + mBadge + '</td></tr>';
            });
            h += '</tbody></table>';
            el.innerHTML = h;
        }

        function dpRenderSummary() {
            var isT = dpLoai() === 'TraNoHomNay';
            var usedImg = new Set(dp.matches.filter(function (m) { return m.imageIdx >= 0; }).map(function (m) { return m.imageIdx; }));
            var nOk = 0, nDiff = 0, nExOnly = 0, nImOnly = 0;
            dp.matches.forEach(function (m, i) {
                if (m.imageIdx < 0) { nExOnly++; return; }
                var ir = dp.imageRows[m.imageIdx], er = dp.excelRows[i];
                var d = isT
                    ? Math.abs((+er.soTienTra || 0) - (+ir.soTienTra || 0)) > 1000
                    : Math.abs((+er.soLuong || 0) - (+ir.soLuongAnh || 0)) > 0.1;
                d ? nDiff++ : nOk++;
            });
            dp.imageRows.forEach(function (_, i) { if (!usedImg.has(i)) nImOnly++; });
            document.getElementById('dpSumMatched').textContent = nOk + ' khớp';
            document.getElementById('dpSumDiff').textContent = nDiff + ' lệch';
            document.getElementById('dpSumExcelOnly').textContent = nExOnly + ' chỉ Excel';
            document.getElementById('dpSumImageOnly').textContent = nImOnly + ' chỉ Ảnh';
            document.getElementById('dpSummaryBar').classList.remove('d-none');
        }

        function dpUpdateSave() {
            var b = document.getElementById('dpBtnSave');
            if (b) b.disabled = !dp.excelRows.length;
        }

        // ---- Save ----
        function dpSave() {
            var isT = dpLoai() === 'TraNoHomNay';
            var rows = dp.excelRows.map(function (row, i) {
                var r = Object.assign({}, row);
                document.querySelectorAll('.dp-ex[data-idx="' + i + '"]').forEach(function (inp) { r[inp.dataset.f] = inp.value; });
                return {
                    tenLai: r.tenLai || null, tenKhach: r.tenKhach || null,
                    soCon: +(r.soCon) || 0, soLuong: +(r.soLuong) || 0,
                    soLuongAnh: r.soLuongAnh != null ? +(r.soLuongAnh) : null,
                    gia: +(r.gia) || 0, thanhTien: +(r.thanhTien) || 0,
                    tienTraLai: +(r.tienTraLai) || 0, soTienTra: +(r.soTienTra) || 0
                };
            });
            var b = document.getElementById('dpBtnSave');
            var orig = b.innerHTML;
            b.disabled = true;
            b.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Đang lưu...';
            fetch('/Home/SaveDualPanelResult', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ngay: dpNgay(), loai: dpLoai(), nguonBanHang: dpNguon(), rows: rows })
            }).then(function (r) { return r.json(); })
            .then(function (d) {
                if (d.ok) {
                    dpMsg('dpSaveMsg', 'success', '✓ Đã lưu ' + d.saved + ' dòng vào DB!');
                    b.innerHTML = '<i class="bi bi-check-circle-fill"></i> Đã lưu';
                    b.className = 'btn btn-sm btn-outline-success';
                } else {
                    dpMsg('dpSaveMsg', 'danger', 'Lỗi: ' + (d.error || 'Không xác định'));
                    b.disabled = false; b.innerHTML = orig;
                }
            }).catch(function (e) {
                dpMsg('dpSaveMsg', 'danger', 'Lỗi kết nối: ' + e.message);
                b.disabled = false; b.innerHTML = orig;
            });
        }

        // ---- Helpers ----
        function dpMsg(id, type, text) {
            var el = document.getElementById(id); if (!el) return;
            if (!text) { el.className = 'px-3 py-1 small d-none'; el.innerHTML = ''; return; }
            var c = { info: 'text-primary', success: 'text-success', warning: 'text-warning', danger: 'text-danger' };
            el.className = 'px-3 py-1 small ' + (c[type] || '');
            el.innerHTML = text;
        }
        function dpPlaceholder(id, color, icon, text) {
            var el = document.getElementById(id);
            if (el) el.innerHTML = '<div class="p-4 text-center text-muted"><i class="bi ' + icon + ' fs-1 d-block text-' + color + ' opacity-25 mb-2"></i>' + text + '</div>';
        }
        function dpHideSummary() { var e = document.getElementById('dpSummaryBar'); if (e) e.classList.add('d-none'); }
        function dpEsc(s) { return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'); }
    })();
});
