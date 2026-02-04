// --- Persistence Utilities ---
function setCookie(name, value, days) {
    let expires = "";
    if (days) {
        const date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        expires = "; expires=" + date.toUTCString();
    }
    document.cookie = name + "=" + (value || "") + expires + "; path=/";
}

function getCookie(name) {
    const nameEQ = name + "=";
    const ca = document.cookie.split(';');
    for (let i = 0; i < ca.length; i++) {
        let c = ca[i];
        while (c.charAt(0) == ' ') c = c.substring(1, c.length);
        if (c.indexOf(nameEQ) == 0) return c.substring(nameEQ.length, c.length);
    }
    return null;
}

// --- Chat Logic ---
function openComments(btn) {
    const id = btn.getAttribute('data-id');
    const title = btn.getAttribute('data-title');
    document.getElementById('commentModalTitle').innerText = title;
    document.getElementById('commentRecId').value = id;

    // Auto-fill author name from cookie
    const savedName = getCookie('commenterName');
    if (savedName) {
        document.getElementById('commentAuthor').value = savedName;
    }

    // Reset List
    const listDiv = document.getElementById('commentsList');
    listDiv.innerHTML = '<div class="text-center text-muted"><div class="spinner-border spinner-border-sm" role="status"></div> Loading...</div>';

    // Show Modal
    const modal = new bootstrap.Modal(document.getElementById('commentModal'));
    modal.show();

    // Fetch Comments (Use Index handler)
    fetch(`/?handler=Comments&id=${id}`)
        .then(res => res.json())
        .then(data => {
            if (!data || data.length === 0) {
                listDiv.innerHTML = '<div class="text-muted text-center small py-3">No comments yet. Be the first!</div>';
            } else {
                listDiv.innerHTML = data.map(c => `
                    <div class="mb-2 border-bottom pb-2">
                        <div class="d-flex justify-content-between align-items-center">
                            <strong class="small text-primary">${escapeHtml(c.author)}</strong>
                            <span class="text-muted" style="font-size:0.7em">${new Date(c.createdAt).toLocaleDateString()} ${new Date(c.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                        </div>
                        <div class="small mt-1 text-break">${escapeHtml(c.text)}</div>
                    </div>
                `).join('');
            }
        })
        .catch(err => {
            console.error(err);
            listDiv.innerHTML = '<div class="text-danger small">Failed to load comments.</div>';
        });
}

// Add Comment Listener
document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('addCommentForm');
    if (form) {
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            const btn = form.querySelector('button[type="submit"]');
            const originalText = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Posting...';

            const formData = new FormData(form);
            const author = formData.get('Author');

            // Save Name to Cookie
            if (author && author.trim() !== 'Anonymous') {
                setCookie('commenterName', author, 365);
            }

            const params = new URLSearchParams();
            params.append('NewComment.RecommendationId', formData.get('RecommendationId'));
            params.append('NewComment.Author', author);
            params.append('NewComment.Text', formData.get('Text'));

            // Try to find token. Only works if on page with token. 
            // Index and Details both have forms with token usually?
            // If not, we might fail. But the modal has @Html.AntiForgeryToken() inside the form!
            const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
            const token = tokenInput ? tokenInput.value : '';

            fetch('/?handler=AddComment', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                    'RequestVerificationToken': token
                },
                body: params
            })
                .then(res => {
                    if (!res.ok) throw new Error('Error');
                    return res.json();
                })
                .then(comment => {
                    const listDiv = document.getElementById('commentsList');
                    if (listDiv.innerText.includes('No comments yet')) listDiv.innerHTML = '';

                    const html = `
                    <div class="mb-2 border-bottom pb-2 bg-light-subtle p-2 rounded animate-fade-in shadow-sm">
                        <div class="d-flex justify-content-between align-items-center">
                            <strong class="small text-primary">${escapeHtml(comment.author)}</strong>
                            <span class="text-muted" style="font-size:0.7em">Just now</span>
                        </div>
                        <div class="small mt-1 text-break fw-bold">${escapeHtml(comment.text)}</div>
                    </div>
                `;
                    listDiv.insertAdjacentHTML('afterbegin', html);

                    // Reset form text only (keep author)
                    document.getElementById('commentText').value = '';

                    // Update Badge Count on Index page tiles
                    if (comment.recommendationId) {
                        const countBadge = document.getElementById(`comment-count-${comment.recommendationId}`);
                        if (countBadge) {
                            countBadge.textContent = parseInt(countBadge.textContent || '0') + 1;
                        }
                    }
                })
                .catch(err => {
                    console.error(err);
                    alert('Failed to post comment.');
                })
                .finally(() => {
                    btn.disabled = false;
                    btn.innerHTML = originalContent;
                });
        });
    }

    // Check local storage for likes on load
    document.querySelectorAll('.like-btn').forEach(btn => {
        const id = btn.getAttribute('data-id');
        if (sessionStorage.getItem(`liked_${id}`)) {
            btn.disabled = true;
            btn.classList.add('disabled-liked');
            btn.classList.remove('btn-outline-primary');
            btn.classList.add('btn-primary');
        }
    });
});

// --- Like Logic ---
function likeRecommendation(id, btn) {
    // 1. Check Session Limit
    const sessionKey = `liked_${id}`;
    if (sessionStorage.getItem(sessionKey)) {
        alert("You have already liked this book in this session!");
        return;
    }

    if (!btn) btn = document.getElementById('likeBtn'); // For Details page fallback

    fetch(`/?handler=Like&id=${id}`, {
        method: 'POST',
        headers: {
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
        }
    })
        .then(response => response.json())
        .then(data => {
            // Update Session
            sessionStorage.setItem(sessionKey, 'true');

            // Update UI
            // Index style: btn has .like-count
            const countSpan = btn.querySelector('.like-count') || document.getElementById('likeCount');
            if (countSpan) countSpan.textContent = data.likes;

            btn.classList.remove('btn-outline-primary');
            btn.classList.add('btn-primary');
            btn.disabled = true;
        })
        .catch(err => console.error('Like failed:', err));
}

function escapeHtml(text) {
    if (!text) return "";
    return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}
