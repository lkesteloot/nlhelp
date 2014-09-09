

(function () {

    var showHits = function (hits) {
        var $hits = $(".js-hits");
        $hits.empty();
        for (var i = 0; i < hits.length; i++) {
            var hit = hits[i];
            $("<p>").addClass("answer").html(markdown.toHTML(hit.answer)).appendTo($hits);
        }
    };

    $(function () {
        var $form = $("#searchForm");
        var $q = $form.find("input[name=q]");
        var $answer = $(".answer");

        $form.submit(function (event) {
            event.preventDefault();

            var query = $.trim($q.val());
            if (query === "") {
                return;
            }

            $.ajax("/search", {
                data: {
                    q: query
                },
                dataType: "json",
                error: function (e) {
                    console.log("Error: " + JSON.stringify(e));
                },
                success: function (data) {
                    showHits(data.entries);
                }
            });
        });
    });
})();
