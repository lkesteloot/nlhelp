

(function () {
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
                error: function () {
                    console.log("Error");
                },
                success: function (data) {
                    $answer.text(data.text);
                }
            });
        });
    });
})();
