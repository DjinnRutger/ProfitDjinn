from flask import Blueprint, render_template, request, redirect, url_for, flash
from flask_login import login_required

from app.extensions import db
from app.models.service_item import ServiceItem
from app.forms.item_form import ItemForm
from app.utils.decorators import permission_required

items_bp = Blueprint("items", __name__, url_prefix="/items")


@items_bp.route("/")
@login_required
@permission_required("items.view")
def list_items():
    show_inactive = request.args.get("inactive", "0") == "1"
    query = ServiceItem.query
    if not show_inactive:
        query = query.filter_by(is_active=True)
    items = query.order_by(ServiceItem.description).all()
    return render_template(
        "items/list.html",
        items=items,
        show_inactive=show_inactive,
        active_page="items",
    )


@items_bp.route("/new", methods=["GET", "POST"])
@login_required
@permission_required("items.create")
def create():
    form = ItemForm()
    if form.validate_on_submit():
        item = ServiceItem(
            description=form.description.data.strip(),
            price=float(form.price.data),
            is_active=form.is_active.data,
        )
        db.session.add(item)
        db.session.commit()
        flash(f"Item '{item.description}' created.", "success")
        return redirect(url_for("items.list_items"))
    return render_template(
        "items/form.html",
        form=form,
        title="New Item",
        active_page="items",
    )


@items_bp.route("/<int:item_id>/edit", methods=["GET", "POST"])
@login_required
@permission_required("items.edit")
def edit(item_id):
    item = ServiceItem.query.get_or_404(item_id)
    form = ItemForm(obj=item)
    if form.validate_on_submit():
        item.description = form.description.data.strip()
        item.price = float(form.price.data)
        item.is_active = form.is_active.data
        db.session.commit()
        flash(f"Item '{item.description}' updated.", "success")
        return redirect(url_for("items.list_items"))
    return render_template(
        "items/form.html",
        form=form,
        item=item,
        title="Edit Item",
        active_page="items",
    )


@items_bp.route("/<int:item_id>/toggle", methods=["POST"])
@login_required
@permission_required("items.edit")
def toggle(item_id):
    item = ServiceItem.query.get_or_404(item_id)
    item.is_active = not item.is_active
    db.session.commit()
    state = "activated" if item.is_active else "deactivated"
    flash(f"Item '{item.description}' {state}.", "info")
    return redirect(url_for("items.list_items", inactive=request.args.get("inactive", "0")))


@items_bp.route("/<int:item_id>/delete", methods=["POST"])
@login_required
@permission_required("items.delete")
def delete(item_id):
    item = ServiceItem.query.get_or_404(item_id)
    description = item.description
    db.session.delete(item)
    db.session.commit()
    flash(f"Item '{description}' deleted.", "warning")
    return redirect(url_for("items.list_items"))
