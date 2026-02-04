// Progress tracking functions for Details page
function refreshMetadata(id) {
    const btn = document.getElementById('refreshBtn');
    const icon = document.getElementById('refreshIcon');
    const text = document.getElementById('refreshText');
    const progressContainer = document.getElementById('progress-container');
    
    // Show loading state
    btn.disabled = true;
    icon.classList.add('spin');
    text.textContent = 'Starting...';
    progressContainer.classList.remove('d-none');
    
    fetch(`/Details/${id}?handler=StartRefreshMetadata`, {
        method: 'POST',
        headers: {
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
        }
    })
    .then(response => response.json())
    .then(data => {
        if (data.success) {
            startProgressPolling(data.processId, () => {
                // On complete callback
                setTimeout(() => {
                    window.location.reload();
                }, 1500);
            });
        } else {
            text.textContent = 'Failed to start';
            btn.classList.add('btn-danger');
            progressContainer.classList.add('d-none');
            btn.disabled = false;
            icon.classList.remove('spin');
        }
    })
    .catch(error => {
        console.error('Refresh failed:', error);
        text.textContent = 'Error starting refresh';
        btn.classList.add('btn-danger');
        progressContainer.classList.add('d-none');
        btn.disabled = false;
        icon.classList.remove('spin');
    });
}

function discoverSeries(id) {
    const btn = document.getElementById('discoverBtn');
    const icon = document.getElementById('discoverIcon');
    const text = document.getElementById('discoverText');
    const progressContainer = document.getElementById('progress-container');
    
    // Show loading state
    btn.disabled = true;
    icon.classList.add('spin');
    text.textContent = 'Starting...';
    progressContainer.classList.remove('d-none');
    
    fetch(`/Details/${id}?handler=StartDiscoverSeries`, {
        method: 'POST',
        headers: {
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
        }
    })
    .then(response => response.json())
    .then(data => {
        if (data.success) {
            startProgressPolling(data.processId, () => {
                // On complete callback
                setTimeout(() => {
                    window.location.reload();
                }, 1500);
            });
        } else {
            text.textContent = 'Failed to start';
            btn.classList.add('btn-danger');
            progressContainer.classList.add('d-none');
            btn.disabled = false;
            icon.classList.remove('spin');
        }
    })
    .catch(error => {
        console.error('Discovery failed:', error);
        text.textContent = 'Error starting discovery';
        btn.classList.add('btn-danger');
        progressContainer.classList.add('d-none');
        btn.disabled = false;
        icon.classList.remove('spin');
    });
}

function startProgressPolling(processId, onComplete) {
    console.log('Starting progress polling for processId:', processId);
    const interval = setInterval(async () => {
        try {
            const res = await fetch(`?handler=RefreshProgress&processId=${processId}`);
            if (!res.ok) {
                console.error('Progress fetch failed:', res.status);
                return;
            }
            const status = await res.json();
            console.log('Progress update:', status);

            updateDetailsProgress(status.percent, status.message);

            if (status.isComplete || status.isError) {
                clearInterval(interval);
                if (status.isError) {
                    showDetailsError(status.message);
                    resetDetailsButtons();
                } else {
                    showDetailsSuccess(status.message);
                    if (onComplete) onComplete();
                }
            }
        } catch (err) {
            console.error('Progress polling error:', err);
        }
    }, 1000); // Poll every 1s
}

function updateDetailsProgress(percent, msg) {
    console.log(`Updating progress: ${percent}% - ${msg}`);
    const bar = document.getElementById('progress-bar');
    bar.style.width = percent + '%';
    bar.innerText = percent + '%';
    document.getElementById('progress-status').innerText = msg;
}

function showDetailsError(msg) {
    const progressContainer = document.getElementById('progress-container');
    progressContainer.classList.add('d-none');
    alert('Error: ' + msg);
}

function showDetailsSuccess(msg) {
    const statusEl = document.getElementById('progress-status');
    statusEl.innerHTML = '<i class="bi bi-check-circle-fill text-success"></i> ' + msg;
}

function resetDetailsButtons() {
    const refreshBtn = document.getElementById('refreshBtn');
    const discoverBtn = document.getElementById('discoverBtn');
    const refreshIcon = document.getElementById('refreshIcon');
    const discoverIcon = document.getElementById('discoverIcon');
    const progressContainer = document.getElementById('progress-container');
    
    if (refreshBtn) {
        refreshBtn.disabled = false;
        refreshIcon.classList.remove('spin');
        document.getElementById('refreshText').textContent = 'Refresh Book Data';
    }
    if (discoverBtn) {
        discoverBtn.disabled = false;
        discoverIcon.classList.remove('spin');
        document.getElementById('discoverText').textContent = 'Auto-Discover Series Books';
    }
    progressContainer.classList.add('d-none');
}
